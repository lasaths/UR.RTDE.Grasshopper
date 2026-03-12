using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Rhino;

namespace UR.RTDE.Grasshopper
{
    public enum URActionKind { MoveJ, MoveL, Stop, SetDO }

    public class UR_WriteComponent : GH_Component
    {
        internal URActionKind _action = URActionKind.MoveJ;
        private readonly List<string> _log = new List<string>();
        private double _stopDecel = 2.0;
        private readonly object _sessionLock = new object();
        private readonly object _stateLock = new object();
        private URSession _lastSession;

        private bool _isRunning = false;
        private int _currentIndex = 0; // 1-based while running; 0 when idle.
        private int _totalCount = 0;
        private int _lastRunId = 0;
        private bool _donePulsePending = false;
        private bool _previousExecute = false;
        private bool _lastOk = true;
        private string _lastMessage = "Idle";
        private bool _refreshQueued = false;

        internal static readonly string[] ActionModes = { "MoveJ", "MoveL", "Stop", "SetDO" };

        public UR_WriteComponent()
          : base("UR Write", "URWrite",
            "Send commands to the robot via RTDE.",
            "UR", "RTDE")
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new UR_CommandAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "O", "True if command succeeded.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Message or error.", GH_ParamAccess.item);
            p.AddBooleanParameter("Running", "R", "True while a MoveJ/MoveL sequence is executing.", GH_ParamAccess.item);
            p.AddIntegerParameter("CurrentIndex", "I", "Current 1-based target index; 0 when idle.", GH_ParamAccess.item);
            p.AddIntegerParameter("Total", "T", "Total target count for active/last sequence.", GH_ParamAccess.item);
            p.AddBooleanParameter("Done", "D", "True for one solve when a sequence completes successfully.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            URSessionGoo goo = null;
            var hasSession = da.GetData(0, ref goo);
            var session = goo?.Value;

            if (session != null && session.IsConnected)
            {
                lock (_sessionLock)
                {
                    _lastSession = session;
                }
            }

            try
            {
                switch (_action)
                {
                    case URActionKind.MoveJ:
                        HandleMoveJ(da, hasSession, session);
                        break;

                    case URActionKind.MoveL:
                        HandleMoveL(da, hasSession, session);
                        break;

                    case URActionKind.Stop:
                        if (session == null || !session.IsConnected)
                        {
                            SetFailedState("Session not connected");
                            WriteStateOutputs(da);
                            return;
                        }

                        double decel = 2.0;
                        da.GetData(1, ref decel);
                        _stopDecel = decel;
                        bool stopJ = session.StopJ(decel);
                        bool stopL = session.StopL(decel);
                        bool stopResult = stopJ || stopL;
                        var stopError = session.LastError ?? "Unknown error";
                        SetStoppedState(stopResult ? $"Stop sent (decel {decel})" : $"Stop failed: {stopError}", stopResult);
                        WriteStateOutputs(da);
                        break;

                    case URActionKind.SetDO:
                        if (session == null || !session.IsConnected)
                        {
                            SetFailedState("Session not connected");
                            WriteStateOutputs(da);
                            return;
                        }

                        int pin = 0;
                        bool val = false;
                        da.GetData(1, ref pin);
                        da.GetData(2, ref val);
                        bool setDoResult = session.SetStandardDigitalOut(pin, val);
                        SetIdleState(setDoResult, setDoResult
                            ? $"Digital output {pin} set to {val}"
                            : $"SetDO failed: {session.LastError ?? "Unknown error"}");
                        WriteStateOutputs(da);
                        break;

                    default:
                        SetFailedState("Not implemented");
                        WriteStateOutputs(da);
                        return;
                }
            }
            catch (Exception ex)
            {
                SetFailedState(ex.Message);
                WriteStateOutputs(da);
            }
        }

        private void HandleMoveJ(IGH_DataAccess da, bool hasSession, URSession session)
        {
            bool execute = false;
            da.GetData(4, ref execute);
            bool risingEdge = execute && !_previousExecute;
            _previousExecute = execute;

            bool running;
            lock (_stateLock) running = _isRunning;
            if (running || !risingEdge)
            {
                WriteStateOutputs(da);
                return;
            }

            if (!hasSession || session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            var jointsParam = Params.Input[1];
            var jointsData = jointsParam.VolatileData;
            if (jointsData.PathCount == 0 || jointsData.DataCount == 0)
            {
                SetFailedState("No joint data provided");
                WriteStateOutputs(da);
                return;
            }

            double speed = 1.05, accel = 1.4;
            da.GetData(2, ref speed);
            da.GetData(3, ref accel);

            var waypoints = new List<double[]>();
            for (int i = 0; i < jointsData.PathCount; i++)
            {
                var branch = jointsData.get_Branch(i);
                if (branch.Count >= 6)
                {
                    var joints = new double[6];
                    for (int j = 0; j < 6; j++)
                    {
                        if (branch[j] is global::Grasshopper.Kernel.Types.GH_Number ghNum) joints[j] = ghNum.Value;
                        else if (branch[j] is double d) joints[j] = d;
                        else
                        {
                            SetFailedState($"Branch {i}: Invalid joint value at index {j}");
                            WriteStateOutputs(da);
                            return;
                        }
                    }
                    waypoints.Add(joints);
                }
                else if (branch.Count > 0)
                {
                    SetFailedState($"Branch {i}: Expected 6 joint values, got {branch.Count}");
                    WriteStateOutputs(da);
                    return;
                }
            }

            if (waypoints.Count == 0)
            {
                SetFailedState("Each branch must contain exactly 6 joint angles");
                WriteStateOutputs(da);
                return;
            }

            var snapshot = new List<double[]>(waypoints.Count);
            foreach (var wp in waypoints) snapshot.Add((double[])wp.Clone());

            int runId;
            lock (_stateLock)
            {
                _isRunning = true;
                _currentIndex = 0;
                _totalCount = snapshot.Count;
                _lastRunId++;
                runId = _lastRunId;
                _donePulsePending = false;
                _lastOk = true;
                _lastMessage = "Executing 0/" + _totalCount;
            }

            AddLog(_lastMessage);
            WriteStateOutputs(da);
            RequestRefresh();

            Task.Run(() => ExecuteMoveJRun(runId, session, snapshot, speed, accel));
        }

        private void HandleMoveL(IGH_DataAccess da, bool hasSession, URSession session)
        {
            bool execute = false;
            da.GetData(5, ref execute);
            bool risingEdge = execute && !_previousExecute;
            _previousExecute = execute;

            bool running;
            lock (_stateLock) running = _isRunning;
            if (running || !risingEdge)
            {
                WriteStateOutputs(da);
                return;
            }

            if (!hasSession || session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            var poseParam = Params.Input[1];
            var planeParam = Params.Input[2];
            var poseData = poseParam.VolatileData;
            var planeData = planeParam.VolatileData;

            double speed = 0.25, accel = 1.2;
            da.GetData(3, ref speed);
            da.GetData(4, ref accel);

            var poses = new List<double[]>();
            if (planeData.PathCount > 0 && planeData.DataCount > 0)
            {
                for (int i = 0; i < planeData.PathCount; i++)
                {
                    var branch = planeData.get_Branch(i);
                    foreach (var item in branch)
                    {
                        if (item is global::Grasshopper.Kernel.Types.GH_Plane ghPlane && ghPlane.Value.IsValid)
                            poses.Add(PoseUtils.PlaneToPose(ghPlane.Value));
                    }
                }
            }
            else if (poseData.PathCount > 0 && poseData.DataCount > 0)
            {
                for (int i = 0; i < poseData.PathCount; i++)
                {
                    var branch = poseData.get_Branch(i);
                    if (branch.Count >= 6)
                    {
                        var pose = new double[6];
                        for (int j = 0; j < 6; j++)
                        {
                            if (TryExtractDouble(branch[j], out var value))
                            {
                                pose[j] = value;
                            }
                            else
                            {
                                SetFailedState($"Branch {i}: Invalid pose value at index {j}");
                                WriteStateOutputs(da);
                                return;
                            }
                        }
                        poses.Add(pose);
                    }
                    else if (branch.Count > 0)
                    {
                        SetFailedState($"Branch {i}: Expected 6 pose values, got {branch.Count}");
                        WriteStateOutputs(da);
                        return;
                    }
                }
            }

            if (poses.Count == 0)
            {
                SetFailedState("Provide target Plane(s) or pose list(s) [x,y,z,rx,ry,rz]");
                WriteStateOutputs(da);
                return;
            }

            var snapshot = new List<double[]>(poses.Count);
            foreach (var p in poses) snapshot.Add((double[])p.Clone());

            int runId;
            lock (_stateLock)
            {
                _isRunning = true;
                _currentIndex = 0;
                _totalCount = snapshot.Count;
                _lastRunId++;
                runId = _lastRunId;
                _donePulsePending = false;
                _lastOk = true;
                _lastMessage = "Executing 0/" + _totalCount;
            }

            AddLog(_lastMessage);
            WriteStateOutputs(da);
            RequestRefresh();

            Task.Run(() => ExecuteMoveLRun(runId, session, snapshot, speed, accel));
        }

        private void ExecuteMoveJRun(int runId, URSession session, List<double[]> waypoints, double speed, double accel)
        {
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (!TrySetProgress(runId, i + 1)) return;

                bool ok;
                try
                {
                    ok = session.MoveJ(waypoints[i], speed, accel, false);
                }
                catch (Exception ex)
                {
                    FinishRun(runId, false, "Failed: " + ex.Message, false);
                    return;
                }

                if (!ok)
                {
                    var error = session.LastError ?? "Unknown error";
                    FinishRun(runId, false, $"Failed at {i + 1}/{waypoints.Count}: {error}", false);
                    return;
                }
            }

            FinishRun(runId, true, $"Completed {waypoints.Count}/{waypoints.Count}", true);
        }

        private void ExecuteMoveLRun(int runId, URSession session, List<double[]> poses, double speed, double accel)
        {
            for (int i = 0; i < poses.Count; i++)
            {
                if (!TrySetProgress(runId, i + 1)) return;

                bool ok;
                try
                {
                    ok = session.MoveL(poses[i], speed, accel, false);
                }
                catch (Exception ex)
                {
                    FinishRun(runId, false, "Failed: " + ex.Message, false);
                    return;
                }

                if (!ok)
                {
                    var error = session.LastError ?? "Unknown error";
                    FinishRun(runId, false, $"Failed at {i + 1}/{poses.Count}: {error}", false);
                    return;
                }
            }

            FinishRun(runId, true, $"Completed {poses.Count}/{poses.Count}", true);
        }

        private bool TrySetProgress(int runId, int index)
        {
            lock (_stateLock)
            {
                if (!_isRunning || runId != _lastRunId) return false;
                _currentIndex = index;
                _lastOk = true;
                _lastMessage = $"Executing {index}/{_totalCount}";
            }

            RequestRefresh();
            return true;
        }

        private void FinishRun(int runId, bool ok, string message, bool pulseDone)
        {
            lock (_stateLock)
            {
                if (runId != _lastRunId) return;
                _isRunning = false;
                _lastOk = ok;
                _lastMessage = message;
                _donePulsePending = pulseDone;
                if (ok) _currentIndex = _totalCount;
            }

            AddLog(message);
            RequestRefresh();
        }

        private void SetIdleState(bool ok, string message)
        {
            lock (_stateLock)
            {
                _isRunning = false;
                _currentIndex = 0;
                _totalCount = 0;
                _donePulsePending = false;
                _lastOk = ok;
                _lastMessage = message;
            }

            AddLog(message);
        }

        private void SetStoppedState(string message, bool ok)
        {
            lock (_stateLock)
            {
                _isRunning = false;
                _currentIndex = 0;
                _totalCount = 0;
                _donePulsePending = false;
                _lastRunId++;
                _lastOk = ok;
                _lastMessage = message;
            }

            AddLog(message);
        }

        private void SetFailedState(string message)
        {
            lock (_stateLock)
            {
                _isRunning = false;
                _currentIndex = 0;
                _totalCount = 0;
                _lastRunId++;
                _donePulsePending = false;
                _lastOk = false;
                _lastMessage = message;
            }

            AddLog("Error: " + message);
        }

        private static bool TryExtractDouble(object value, out double numeric)
        {
            if (value is global::Grasshopper.Kernel.Types.GH_Number ghNum)
            {
                numeric = ghNum.Value;
                return true;
            }

            if (value is double d)
            {
                numeric = d;
                return true;
            }

            numeric = 0.0;
            return false;
        }

        private void AddLog(string message)
        {
            lock (_sessionLock)
            {
                _log.Clear();
                _log.Add($"{DateTime.Now:HH:mm:ss} - {message}");
            }
        }

        private void RequestRefresh()
        {
            lock (_stateLock)
            {
                if (_refreshQueued) return;
                _refreshQueued = true;
            }

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                try
                {
                    var doc = OnPingDocument();
                    if (doc != null)
                    {
                        doc.ScheduleSolution(5, d => ExpireSolution(false));
                    }
                }
                finally
                {
                    lock (_stateLock)
                    {
                        _refreshQueued = false;
                    }
                }
            }));
        }

        private void WriteStateOutputs(IGH_DataAccess da)
        {
            bool ok;
            string message;
            bool running;
            int current;
            int total;
            bool done;

            lock (_stateLock)
            {
                ok = _lastOk;
                message = _lastMessage;
                running = _isRunning;
                current = _currentIndex;
                total = _totalCount;
                done = _donePulsePending;
                if (_donePulsePending) _donePulsePending = false;
            }

            da.SetData(0, ok);
            da.SetData(1, message);
            da.SetData(2, running);
            da.SetData(3, current);
            da.SetData(4, total);
            da.SetData(5, done);

            if (done) RequestRefresh();
        }

        internal void SetAction(int index)
        {
            if (index >= 0 && index < ActionModes.Length)
            {
                _action = (URActionKind)index;
                _previousExecute = false;
                RebuildInputsForAction();
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.rocket-launch-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("2233737c-7ba5-4bf9-9c14-924c5d7077cd");

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            RebuildInputsForAction();
        }

        internal void RebuildInputsForAction()
        {
            if (Params == null) return;

            while (Params.Input.Count > 1)
            {
                var toRemove = Params.Input[1];
                Params.UnregisterInputParameter(toRemove, true);
            }

            Param_Number Num(string name, string nick, string desc, GH_ParamAccess access, double? def = null, bool optional = true)
            {
                var p = new Param_Number { Name = name, NickName = nick, Description = desc, Access = access, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }

            Param_Boolean Bool(string name, string nick, string desc, bool? def = null, bool optional = true)
            {
                var p = new Param_Boolean { Name = name, NickName = nick, Description = desc, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }

            Param_Integer Int(string name, string nick, string desc, int? def = null, bool optional = true)
            {
                var p = new Param_Integer { Name = name, NickName = nick, Description = desc, Optional = optional };
                if (def.HasValue) p.SetPersistentData(def.Value);
                return p;
            }

            switch (_action)
            {
                case URActionKind.MoveJ:
                    Params.RegisterInputParam(Num("Joints", "Q", "Joint target angles (rad)", GH_ParamAccess.list, null, false));
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed", GH_ParamAccess.item, 1.05));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration", GH_ParamAccess.item, 1.4));
                    Params.RegisterInputParam(Bool("Execute", "E", "Start sequence on rising edge (false->true)", false, false));
                    break;

                case URActionKind.MoveL:
                    var pose = Num("Pose", "P", "TCP pose [x,y,z,rx,ry,rz] (m,rad)", GH_ParamAccess.list);
                    pose.Optional = true;
                    Params.RegisterInputParam(pose);
                    Params.RegisterInputParam(new Param_Plane { Name = "Target", NickName = "T", Description = "Target Plane (alternative to Pose)", Optional = true });
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed", GH_ParamAccess.item, 0.25));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration", GH_ParamAccess.item, 1.2));
                    Params.RegisterInputParam(Bool("Execute", "E", "Start sequence on rising edge (false->true)", false, false));
                    break;

                case URActionKind.Stop:
                    Params.RegisterInputParam(Num("Deceleration", "D", "Stop deceleration", GH_ParamAccess.item, _stopDecel, false));
                    break;

                case URActionKind.SetDO:
                    Params.RegisterInputParam(Int("Pin", "I", "Digital output pin", 0, false));
                    Params.RegisterInputParam(Bool("Value", "B", "Digital output value", false, false));
                    break;
            }

            Params.OnParametersChanged();
            ExpireSolution(true);
        }

        internal void TriggerStopFromButton()
        {
            URSession session;
            double decel;

            lock (_sessionLock)
            {
                session = _lastSession;
                decel = _stopDecel;
            }

            if (session == null || !session.IsConnected)
            {
                RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Stop ignored: no connected session");
                    ExpireSolution(false);
                }));
                return;
            }

            Task.Run(() =>
            {
                bool okJ = false;
                bool okL = false;
                string error = null;

                try { okJ = session.StopJ(decel); if (!okJ && session.LastError != null) error = session.LastError; }
                catch (Exception ex) { error = ex.Message; }

                try { okL = session.StopL(decel); if (!okL && session.LastError != null) error = session.LastError ?? error; }
                catch (Exception ex) { error ??= ex.Message; }

                bool ok = okJ || okL;
                var message = ok ? $"Stop sent (decel {decel})" : $"Stop failed: {error ?? "Unknown error"}";
                SetStoppedState(message, ok);

                RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    AddRuntimeMessage(ok ? GH_RuntimeMessageLevel.Remark : GH_RuntimeMessageLevel.Error, message);
                    ExpireSolution(false);
                }));
            });
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("action", (int)_action);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("action")) _action = (URActionKind)reader.GetInt32("action");
            return base.Read(reader);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
    }
    internal sealed class UR_CommandAttributes : GH_ComponentAttributes
    {
        private RectangleF _dropdownBounds;
        private RectangleF _dropdownButtonBounds;
        private RectangleF _stopButtonBounds;
        private List<RectangleF> _dropdownItemBounds;
        private bool _dropdownOpen = false;
        private bool _dropdownHover = false;
        private bool _stopMouseDown;
        private bool _stopMouseOver;
        private int _hoverItemIndex = -1;

        public UR_CommandAttributes(UR_WriteComponent owner) : base(owner)
        {
        }

        private UR_WriteComponent CommandComponent => Owner as UR_WriteComponent;

        protected override void Layout()
        {
            base.Layout();

            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;
            var s = 4f / scale;
            var buttonHeight = 28f / scale;
            var buttonSpacing = 6f / scale;

            var body = Bounds;
            // Only show stop button when in Stop action mode
            bool showStopButton = CommandComponent._action == URActionKind.Stop;
            var reservedHeight = (showStopButton ? buttonHeight + buttonSpacing : 0) + buttonHeight + (4f * s);
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var bandTop = body.Bottom - reservedHeight;
            var elementWidth = Math.Max(60f / scale, body.Width - 6f * s);
            var elementX = body.X + (body.Width - elementWidth) * 0.5f;

            float currentY = bandTop + (2f * s);

            // Stop button first (only in Stop mode, above dropdown)
            if (showStopButton)
            {
                _stopButtonBounds = new RectangleF(elementX, currentY, elementWidth, buttonHeight);
                currentY += buttonHeight + buttonSpacing;
            }
            else
            {
                _stopButtonBounds = RectangleF.Empty;
            }

            // Dropdown (below stop button if it exists, otherwise at the top)
            _dropdownBounds = new RectangleF(elementX, currentY, elementWidth, buttonHeight);
            _dropdownButtonBounds = new RectangleF(_dropdownBounds.Right - buttonHeight, _dropdownBounds.Y, buttonHeight, buttonHeight);

            // Dropdown items (only when open)
            _dropdownItemBounds = new List<RectangleF>();
            if (_dropdownOpen)
            {
                for (int i = 0; i < UR_WriteComponent.ActionModes.Length; i++)
                {
                    _dropdownItemBounds.Add(new RectangleF(
                        _dropdownBounds.X,
                        _dropdownBounds.Bottom + (i * buttonHeight),
                        _dropdownBounds.Width,
                        buttonHeight));
                }

                Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, 
                    Bounds.Height + (UR_WriteComponent.ActionModes.Length * buttonHeight));
            }
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;

            // Draw STOP button first (only in Stop mode, above dropdown)
            if (CommandComponent._action == URActionKind.Stop && !_stopButtonBounds.IsEmpty)
            {
                var stopBg = Color.FromArgb(239, 68, 68);
                if (_stopMouseDown) stopBg = Darken(stopBg, 0.2);
                else if (_stopMouseOver) stopBg = Color.FromArgb(
                    Math.Min(255, stopBg.R + 20),
                    Math.Min(255, stopBg.G + 20),
                    Math.Min(255, stopBg.B + 20));

                var cornerRadius = (int)Math.Max(2, Math.Round(8f / scale));
                using (var path = RoundedRect(_stopButtonBounds, cornerRadius))
                {
                    graphics.FillPath(new SolidBrush(stopBg), path);
                    graphics.DrawPath(new Pen(Darken(stopBg, 0.4), 1.2f), path);
                }

                var buttonFont = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size / scale, FontStyle.Bold);
                graphics.DrawString("STOP", buttonFont, Brushes.White, _stopButtonBounds, GH_TextRenderingConstants.CenterCenter);
                buttonFont.Dispose();
            }

            // Draw dropdown (below stop button if in Stop mode, otherwise at the top)
            var cornerRadiusDropdown = (int)Math.Max(2, Math.Round(8f / scale));
            var dropdownBg = _dropdownHover ? Color.FromArgb(180, 180, 180) : Color.LightGray;
            
            using (var path = RoundedRect(_dropdownBounds, cornerRadiusDropdown))
            {
                graphics.FillPath(new SolidBrush(dropdownBg), path);
                graphics.DrawPath(new Pen(Darken(dropdownBg, 0.3), 1.2f), path);
            }

            // Text centered in the dropdown (excluding arrow area)
            var font = new Font(GH_FontServer.FamilyStandard, 8f / scale, FontStyle.Regular);
            var textBounds = new RectangleF(_dropdownBounds.X, _dropdownBounds.Y, 
                _dropdownBounds.Width - _dropdownButtonBounds.Width, _dropdownBounds.Height);
            var selectedText = UR_WriteComponent.ActionModes[(int)CommandComponent._action];
            graphics.DrawString(selectedText, font, Brushes.Black, textBounds, GH_TextRenderingConstants.CenterCenter);

            // Draw dropdown arrow
            DrawDropDownArrow(graphics, new PointF(
                _dropdownButtonBounds.X + _dropdownButtonBounds.Width / 2,
                _dropdownButtonBounds.Y + _dropdownButtonBounds.Height / 2), Color.DarkGray);

            // Draw dropdown items if open
            if (_dropdownOpen)
            {
                for (int i = 0; i < _dropdownItemBounds.Count; i++)
                {
                    var itemBounds = _dropdownItemBounds[i];
                    var itemBg = i == _hoverItemIndex ? Color.FromArgb(200, 200, 200) : Color.LightGray;
                    
                    using (var itemPath = RoundedRect(itemBounds, cornerRadiusDropdown))
                    {
                        graphics.FillPath(new SolidBrush(itemBg), itemPath);
                        graphics.DrawPath(new Pen(Color.Gray, 0.8f), itemPath);
                    }
                    
                    graphics.DrawString(UR_WriteComponent.ActionModes[i], font, Brushes.Black, itemBounds, GH_TextRenderingConstants.CenterCenter);
                }
            }

            font.Dispose();
        }

        private void DrawDropDownArrow(Graphics graphics, PointF center, Color colour)
        {
            using (var pen = new Pen(colour, 2f))
            {
                graphics.DrawLines(pen, new PointF[]
                {
                    new PointF(center.X - 4, center.Y - 2),
                    new PointF(center.X, center.Y + 2),
                    new PointF(center.X + 4, center.Y - 2)
                });
            }
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(c.A, (int)(c.R * (1 - amount)), (int)(c.G * (1 - amount)), (int)(c.B * (1 - amount)));
        }

        private static GraphicsPath RoundedRect(RectangleF bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));

            if (radius == 0) { path.AddRectangle(bounds); return path; }

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseDown(sender, e);

            if (e.Button == MouseButtons.Left)
            {
                // Stop button only works in Stop mode
                if (CommandComponent._action == URActionKind.Stop && !_stopButtonBounds.IsEmpty && _stopButtonBounds.Contains(e.CanvasLocation))
                {
                    _stopMouseDown = true;
                    Owner.OnDisplayExpired(false);
                    return GH_ObjectResponse.Capture;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseUp(sender, e);

            if (e.Button == MouseButtons.Left)
            {
                // Stop button (only in Stop mode)
                if (_stopMouseDown && !_stopButtonBounds.IsEmpty && _stopButtonBounds.Contains(e.CanvasLocation))
                {
                    _stopMouseDown = false;
                    _stopMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    
                    // Trigger the stop action
                    if (CommandComponent != null)
                    {
                        CommandComponent.TriggerStopFromButton();
                    }
                    
                    Owner.OnDisplayExpired(false);
                    return GH_ObjectResponse.Release;
                }
                _stopMouseDown = false;

                // Dropdown toggle
                if (_dropdownBounds.Contains(e.CanvasLocation))
                {
                    _dropdownOpen = !_dropdownOpen;
                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Handled;
                }

                // Dropdown item selection
                if (_dropdownOpen)
                {
                    for (int i = 0; i < _dropdownItemBounds.Count; i++)
                    {
                        if (_dropdownItemBounds[i].Contains(e.CanvasLocation))
                        {
                            _dropdownOpen = false;
                            CommandComponent?.SetAction(i);
                            Owner.ExpireSolution(true);
                            return GH_ObjectResponse.Handled;
                        }
                    }
                    _dropdownOpen = false;
                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Handled;
                }
            }

            return base.RespondToMouseUp(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseMove(sender, e);

            bool wasDropdownHover = _dropdownHover;
            bool wasStopOver = _stopMouseOver;
            int wasHoverIndex = _hoverItemIndex;

            _dropdownHover = _dropdownBounds.Contains(e.CanvasLocation);
            // Stop button hover only works in Stop mode
            _stopMouseOver = CommandComponent._action == URActionKind.Stop && !_stopButtonBounds.IsEmpty && _stopButtonBounds.Contains(e.CanvasLocation);
            _hoverItemIndex = -1;

            if (_dropdownOpen)
            {
                for (int i = 0; i < _dropdownItemBounds.Count; i++)
                {
                    if (_dropdownItemBounds[i].Contains(e.CanvasLocation))
                    {
                        _hoverItemIndex = i;
                        break;
                    }
                }
            }

            if (_dropdownHover != wasDropdownHover || _stopMouseOver != wasStopOver || _hoverItemIndex != wasHoverIndex)
            {
                Owner.OnDisplayExpired(false);
            }

            if (_dropdownHover || _stopMouseOver || _hoverItemIndex >= 0)
            {
                sender.Cursor = Cursors.Hand;
                return GH_ObjectResponse.Capture;
            }
            else
            {
                global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                return GH_ObjectResponse.Release;
            }
        }
    }
}
