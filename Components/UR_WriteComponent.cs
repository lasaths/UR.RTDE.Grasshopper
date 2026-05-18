using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using Rhino;
using Rhino.Geometry;

namespace UR.RTDE.Grasshopper
{
    public enum URActionKind
    {
        MoveJ,
        MoveL,
        Stop,
        SetDO,
        SetAO,
        SetToolDO,
        SetTCP,
        SetPayload,
        SpeedStop,
        ServoStop
    }

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
        private bool _lastOk = true;
        private string _lastMessage = "Idle";
        private bool _refreshQueued = false;
        private bool _runRequested = false;
        private bool _autoSend = false;
        private bool _autoSendInitialized = false;
        private string _lastMotionSignature = string.Empty;
        private GH_RuntimeMessageLevel? _stickyRuntimeLevel;
        private string _stickyRuntimeMessage;

        private const int MotionWaitTimeoutMs = 120_000;

        internal static readonly string[] ActionModes =
        {
            "MoveJ", "MoveL", "Stop", "Set DO", "Set AO", "Tool DO",
            "Set TCP", "Set Payload", "Speed Stop", "Servo Stop"
        };

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
                        HandleSetDO(da, session);
                        break;

                    case URActionKind.SetAO:
                        HandleSetAO(da, session);
                        break;

                    case URActionKind.SetToolDO:
                        HandleSetToolDO(da, session);
                        break;

                    case URActionKind.SetTCP:
                        HandleSetTCP(da, session);
                        break;

                    case URActionKind.SetPayload:
                        HandleSetPayload(da, session);
                        break;

                    case URActionKind.SpeedStop:
                        HandleSpeedStop(da, session);
                        break;

                    case URActionKind.ServoStop:
                        HandleServoStop(da, session);
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

        private bool ShouldStartMotion(string signature)
        {
            lock (_stateLock)
            {
                if (_runRequested)
                {
                    _runRequested = false;
                    _autoSendInitialized = true;
                    _lastMotionSignature = signature;
                    return true;
                }

                if (!_autoSend)
                    return false;

                if (!_autoSendInitialized)
                {
                    _autoSendInitialized = true;
                    _lastMotionSignature = signature;
                    return false;
                }

                if (string.Equals(_lastMotionSignature, signature, StringComparison.Ordinal))
                    return false;

                _lastMotionSignature = signature;
                return true;
            }
        }

        private bool ShouldShowAutoSendIdleMessage()
        {
            lock (_stateLock)
                return _autoSend && _autoSendInitialized;
        }

        private void HandleSetDO(IGH_DataAccess da, URSession session)
        {
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
            bool ok = session.SetStandardDigitalOut(pin, val);
            SetIdleState(ok, ok
                ? $"Digital output {pin} set to {val}"
                : $"SetDO failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private void HandleSetAO(IGH_DataAccess da, URSession session)
        {
            if (session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            int index = 0;
            double value = 0.0;
            bool useCurrent = false;
            da.GetData(1, ref index);
            da.GetData(2, ref value);
            da.GetData(3, ref useCurrent);
            var mode = useCurrent ? URAnalogOutputMode.Current : URAnalogOutputMode.Voltage;
            bool ok = session.SetAnalogOutput(index, value, mode);
            SetIdleState(ok, ok
                ? $"Analog output {index} set to {value} ({mode})"
                : $"SetAO failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private void HandleSetToolDO(IGH_DataAccess da, URSession session)
        {
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
            bool ok = session.SetToolDigitalOut(pin, val);
            SetIdleState(ok, ok
                ? $"Tool digital output {pin} set to {val}"
                : $"Tool DO failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private void HandleSetTCP(IGH_DataAccess da, URSession session)
        {
            if (session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            if (!TryGetSingleTcpPose(da, out var pose))
            {
                WriteStateOutputs(da);
                return;
            }

            bool ok = session.SetTcp(pose);
            SetIdleState(ok, ok
                ? $"TCP updated: {FormatVector(pose)}"
                : $"SetTCP failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private void HandleSetPayload(IGH_DataAccess da, URSession session)
        {
            if (session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            double mass = 1.0;
            da.GetData(1, ref mass);
            var cog = new Point3d(0, 0, 0);
            da.GetData(2, ref cog);
            var cogArr = new[] { cog.X, cog.Y, cog.Z };
            bool ok = session.SetPayload(mass, cogArr);
            SetIdleState(ok, ok
                ? $"Payload set: mass {mass}, CoG [{cog.X:0.###}, {cog.Y:0.###}, {cog.Z:0.###}]"
                : $"SetPayload failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private void HandleSpeedStop(IGH_DataAccess da, URSession session)
        {
            if (session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            double accel = 10.0;
            da.GetData(1, ref accel);
            bool ok = session.SpeedStop(accel);
            SetIdleState(ok, ok
                ? $"SpeedStop sent (accel {accel})"
                : $"SpeedStop failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private void HandleServoStop(IGH_DataAccess da, URSession session)
        {
            if (session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected");
                WriteStateOutputs(da);
                return;
            }

            double accel = 0.5;
            da.GetData(1, ref accel);
            bool ok = session.ServoStop(accel);
            SetIdleState(ok, ok
                ? $"ServoStop sent (accel {accel})"
                : $"ServoStop failed: {session.LastError ?? "Unknown error"}");
            WriteStateOutputs(da);
        }

        private bool TryGetSingleTcpPose(IGH_DataAccess da, out double[] pose)
        {
            pose = null;

            foreach (var inp in Params.Input)
            {
                if (inp is not Param_Plane) continue;
                var planeData = inp.VolatileData;
                if (planeData.PathCount > 0 && planeData.DataCount > 0)
                {
                    foreach (var item in planeData.AllData(true))
                    {
                        if (item is global::Grasshopper.Kernel.Types.GH_Plane ghPlane && ghPlane.Value.IsValid)
                        {
                            pose = PoseUtils.PlaneToPose(ghPlane.Value);
                            return true;
                        }
                    }
                }
            }

            if (Params.Input.Count < 2) return false;
            var poseParam = Params.Input[1];
            if (poseParam is Param_Plane) return false;
            var poseData = poseParam.VolatileData;
            if (poseData.PathCount > 0 && poseData.DataCount > 0)
            {
                var branch = poseData.get_Branch(0);
                if (branch.Count >= 6)
                {
                    pose = new double[6];
                    for (int j = 0; j < 6; j++)
                    {
                        if (!TryExtractDouble(branch[j], out pose[j]))
                        {
                            SetFailedState($"Invalid pose value at index {j}");
                            return false;
                        }
                    }
                    return true;
                }
            }

            SetFailedState("Provide target Plane or pose [x,y,z,rx,ry,rz]");
            return false;
        }

        private void HandleMoveJ(IGH_DataAccess da, bool hasSession, URSession session)
        {
            bool running;
            lock (_stateLock) running = _isRunning;
            if (running)
            {
                WriteStateOutputs(da);
                return;
            }

            if (!hasSession || session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected. Use the Session component's Connect button first.");
                WriteStateOutputs(da);
                return;
            }

            var jointsParam = Params.Input[1];
            var jointsData = jointsParam.VolatileData;
            if (jointsData.PathCount == 0 || jointsData.DataCount == 0)
            {
                SetFailedState("No joint data provided. Supply 6 joint angles in radians or one branch per waypoint.");
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
                            SetFailedState($"Branch {i}: Invalid joint value at index {j}. Input shape: {DescribeInputShape(jointsData)}");
                            WriteStateOutputs(da);
                            return;
                        }
                    }
                    waypoints.Add(joints);
                }
                else if (branch.Count > 0)
                {
                    SetFailedState($"Branch {i}: Expected 6 joint values, got {branch.Count}. Input shape: {DescribeInputShape(jointsData)}");
                    WriteStateOutputs(da);
                    return;
                }
            }

            if (waypoints.Count == 0)
            {
                SetFailedState($"Each branch must contain exactly 6 joint angles. Input shape: {DescribeInputShape(jointsData)}");
                WriteStateOutputs(da);
                return;
            }

            var signature = BuildMoveSignature("MoveJ", speed, accel, waypoints);
            if (!ShouldStartMotion(signature))
            {
                if (!HasStickyRuntimeMessage())
                {
                    if (_autoSend)
                    {
                        if (ShouldShowAutoSendIdleMessage())
                            SetInfoState("Auto Send on. Waiting for new input.");
                        else
                            SetInfoState("Auto Send will send the next new target.");
                    }
                    else
                    {
                        SetInfoState("Press Run to send the current joint target.");
                    }
                }
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
                _lastMessage = $"Executing 0/{_totalCount}. First target: {FormatVector(snapshot[0])}";
            }

            AddLog(_lastMessage);
            WriteStateOutputs(da);
            RequestRefresh();

            Task.Run(() => ExecuteMoveJRun(runId, session, snapshot, speed, accel));
        }

        private void HandleMoveL(IGH_DataAccess da, bool hasSession, URSession session)
        {
            bool running;
            lock (_stateLock) running = _isRunning;
            if (running)
            {
                WriteStateOutputs(da);
                return;
            }

            if (!hasSession || session == null || !session.IsConnected)
            {
                SetFailedState("Session not connected. Use the Session component's Connect button first.");
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
                                SetFailedState($"Branch {i}: Invalid pose value at index {j}. Input shape: {DescribeInputShape(poseData)}");
                                WriteStateOutputs(da);
                                return;
                            }
                        }
                        poses.Add(pose);
                    }
                    else if (branch.Count > 0)
                    {
                        SetFailedState($"Branch {i}: Expected 6 pose values, got {branch.Count}. Input shape: {DescribeInputShape(poseData)}");
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

            var signature = BuildMoveSignature("MoveL", speed, accel, poses);
            if (!ShouldStartMotion(signature))
            {
                if (!HasStickyRuntimeMessage())
                {
                    if (_autoSend)
                    {
                        if (ShouldShowAutoSendIdleMessage())
                            SetInfoState("Auto Send on. Waiting for new input.");
                        else
                            SetInfoState("Auto Send will send the next new target.");
                    }
                    else
                    {
                        SetInfoState("Press Run to send the current target.");
                    }
                }
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
                _lastMessage = $"Executing 0/{_totalCount}. First target: {FormatVector(snapshot[0])}";
            }

            AddLog(_lastMessage);
            WriteStateOutputs(da);
            RequestRefresh();

            Task.Run(() => ExecuteMoveLRun(runId, session, snapshot, speed, accel));
        }

        private void ExecuteMoveJRun(int runId, URSession session, List<double[]> waypoints, double speed, double accel)
        {
            session.PushStreamSendSuppress();
            try
            {
                ExecuteMoveJRunCore(runId, session, waypoints, speed, accel);
            }
            finally
            {
                session.PopStreamSendSuppress();
            }
        }

        private void ExecuteMoveJRunCore(int runId, URSession session, List<double[]> waypoints, double speed, double accel)
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
                    FinishRun(runId, false, $"MoveJ failed at {i + 1}/{waypoints.Count}: {error}. Target: {FormatVector(waypoints[i])}", false);
                    return;
                }

                if (!session.WaitForMotionComplete(MotionWaitTimeoutMs))
                {
                    FinishRun(runId, false, $"MoveJ timeout at {i + 1}/{waypoints.Count}: {session.LastError}", false);
                    return;
                }
            }

            FinishRun(runId, true, $"Completed {waypoints.Count}/{waypoints.Count}", true);
        }

        private void ExecuteMoveLRun(int runId, URSession session, List<double[]> poses, double speed, double accel)
        {
            session.PushStreamSendSuppress();
            try
            {
                ExecuteMoveLRunCore(runId, session, poses, speed, accel);
            }
            finally
            {
                session.PopStreamSendSuppress();
            }
        }

        private void ExecuteMoveLRunCore(int runId, URSession session, List<double[]> poses, double speed, double accel)
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
                    FinishRun(runId, false, $"MoveL failed at {i + 1}/{poses.Count}: {error}. Target: {FormatVector(poses[i])}", false);
                    return;
                }

                if (!session.WaitForMotionComplete(MotionWaitTimeoutMs))
                {
                    FinishRun(runId, false, $"MoveL timeout at {i + 1}/{poses.Count}: {session.LastError}", false);
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
            if (ok) ClearStickyRuntimeMessage();
            else RememberStickyRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
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
            if (ok) ClearStickyRuntimeMessage();
            else RememberStickyRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
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
            if (ok) ClearStickyRuntimeMessage();
            else RememberStickyRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
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
            RememberStickyRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
        }

        private void SetInfoState(string message)
        {
            lock (_stateLock)
            {
                _lastMessage = message;
            }

            ClearStickyRuntimeMessage();
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

        private bool HasStickyRuntimeMessage()
        {
            lock (_stateLock)
                return _stickyRuntimeLevel.HasValue && !string.IsNullOrWhiteSpace(_stickyRuntimeMessage);
        }

        private void ClearStickyRuntimeMessage()
        {
            lock (_stateLock)
            {
                _stickyRuntimeLevel = null;
                _stickyRuntimeMessage = null;
            }
        }

        private void RememberStickyRuntimeMessage(GH_RuntimeMessageLevel level, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            lock (_stateLock)
            {
                _stickyRuntimeLevel = level;
                _stickyRuntimeMessage = message;
            }

            RhinoApp.WriteLine($"[UR Write] {level}: {message}");
        }

        private void EmitStickyRuntimeMessageIfAny()
        {
            GH_RuntimeMessageLevel? level;
            string message;

            lock (_stateLock)
            {
                level = _stickyRuntimeLevel;
                message = _stickyRuntimeMessage;
            }

            if (level.HasValue && !string.IsNullOrWhiteSpace(message))
                AddRuntimeMessage(level.Value, message);
        }

        private static string DescribeInputShape(IGH_Structure data)
        {
            if (data == null) return "no data";

            var branches = new List<string>();
            for (int i = 0; i < data.PathCount; i++)
                branches.Add(data.get_Branch(i).Count.ToString());

            return $"paths={data.PathCount}, items={data.DataCount}, branchSizes=[{string.Join(", ", branches)}]";
        }

        private static string FormatVector(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return "[]";
            return "[" + string.Join(", ", values.Select(v => v.ToString("0.###"))) + "]";
        }

        private static string BuildMoveSignature(string action, double speed, double accel, IEnumerable<double[]> targets)
        {
            var targetText = string.Join(";", targets.Select(FormatVector));
            return $"{action}|{speed:0.########}|{accel:0.########}|{targetText}";
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
            EmitStickyRuntimeMessageIfAny();

            if (done) RequestRefresh();
        }

        internal void SetAction(int index)
        {
            if (index >= 0 && index < ActionModes.Length)
            {
                var nextAction = (URActionKind)index;
                if (_action != nextAction)
                    _autoSend = false;

                _action = nextAction;
                ResetMotionTriggerState();
                RebuildInputsForAction();
            }
        }

        internal void ToggleAutoSend()
        {
            if (_action != URActionKind.MoveJ && _action != URActionKind.MoveL)
                return;

            _autoSend = !_autoSend;
            ResetMotionTriggerState();
            if (_autoSend)
                SetInfoState("Auto Send on. Waiting for new input.");
            else
                SetInfoState("Auto Send off. Use Run to send the current target.");
            ExpireSolution(true);
        }

        internal bool IsMotionAction()
        {
            return _action == URActionKind.MoveJ || _action == URActionKind.MoveL;
        }

        internal bool IsAutoSendEnabled()
        {
            lock (_stateLock)
                return _autoSend;
        }

        private void ResetMotionTriggerState()
        {
            lock (_stateLock)
            {
                _runRequested = false;
                _autoSendInitialized = false;
                _lastMotionSignature = string.Empty;
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

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            if (_action == URActionKind.MoveJ || _action == URActionKind.MoveL)
                Menu_AppendItem(menu, "Auto Send", (s, e) => ToggleAutoSend(), true, _autoSend);
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
                    break;

                case URActionKind.MoveL:
                    var pose = Num("Pose", "P", "TCP pose [x,y,z,rx,ry,rz] (m,rad)", GH_ParamAccess.list);
                    pose.Optional = true;
                    Params.RegisterInputParam(pose);
                    Params.RegisterInputParam(new Param_Plane { Name = "Target", NickName = "T", Description = "Target Plane (alternative to Pose)", Optional = true });
                    Params.RegisterInputParam(Num("Speed", "V", "Motion speed", GH_ParamAccess.item, 0.25));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Motion acceleration", GH_ParamAccess.item, 1.2));
                    break;

                case URActionKind.Stop:
                    Params.RegisterInputParam(Num("Deceleration", "D", "Stop deceleration", GH_ParamAccess.item, _stopDecel, false));
                    break;

                case URActionKind.SetDO:
                    Params.RegisterInputParam(Int("Pin", "I", "Digital output pin", 0, false));
                    Params.RegisterInputParam(Bool("Value", "B", "Digital output value", false, false));
                    break;

                case URActionKind.SetAO:
                    Params.RegisterInputParam(Int("Index", "I", "Analog output index", 0, false));
                    Params.RegisterInputParam(Num("Value", "V", "Analog output value", GH_ParamAccess.item, 0.0, false));
                    Params.RegisterInputParam(Bool("Current", "C", "True = current mode, false = voltage", false, true));
                    break;

                case URActionKind.SetToolDO:
                    Params.RegisterInputParam(Int("Pin", "I", "Tool digital output pin", 0, false));
                    Params.RegisterInputParam(Bool("Value", "B", "Tool digital output value", false, false));
                    break;

                case URActionKind.SetTCP:
                    var tcpPose = Num("Pose", "P", "TCP pose [x,y,z,rx,ry,rz] (m,rad)", GH_ParamAccess.list);
                    tcpPose.Optional = true;
                    Params.RegisterInputParam(tcpPose);
                    Params.RegisterInputParam(new Param_Plane { Name = "Target", NickName = "T", Description = "TCP Plane (alternative to Pose)", Optional = true });
                    break;

                case URActionKind.SetPayload:
                    Params.RegisterInputParam(Num("Mass", "M", "Payload mass (kg)", GH_ParamAccess.item, 1.0, false));
                    Params.RegisterInputParam(new Param_Point { Name = "CoG", NickName = "C", Description = "Center of gravity (document units)", Optional = true });
                    break;

                case URActionKind.SpeedStop:
                    Params.RegisterInputParam(Num("Acceleration", "A", "Speed stop deceleration", GH_ParamAccess.item, 10.0, true));
                    break;

                case URActionKind.ServoStop:
                    Params.RegisterInputParam(Num("Acceleration", "A", "Servo stop deceleration", GH_ParamAccess.item, 0.5, true));
                    break;
            }

            Params.OnParametersChanged();
            ExpireSolution(true);
        }

        internal void TriggerRunFromButton()
        {
            if (!IsMotionAction())
                return;

            if (IsAutoSendEnabled())
            {
                ToggleAutoSend();
                return;
            }

            lock (_stateLock)
            {
                if (_isRunning) return;
                _runRequested = true;
            }

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
            _autoSend = false;
            ResetMotionTriggerState();
            return base.Read(reader);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
    }
    internal sealed class UR_CommandAttributes : GH_ComponentAttributes
    {
        private RectangleF _dropdownBounds;
        private RectangleF _dropdownButtonBounds;
        private RectangleF _runButtonBounds;
        private RectangleF _stopButtonBounds;
        private List<RectangleF> _dropdownItemBounds;
        private bool _dropdownOpen = false;
        private bool _dropdownHover = false;
        private bool _runMouseDown;
        private bool _runMouseOver;
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
            bool showRunButton = CommandComponent._action == URActionKind.MoveJ || CommandComponent._action == URActionKind.MoveL;
            bool showStopButton = CommandComponent._action == URActionKind.Stop;
            bool showActionButton = showRunButton || showStopButton;
            var reservedHeight = (showActionButton ? buttonHeight + buttonSpacing : 0) + buttonHeight + (4f * s);
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var bandTop = body.Bottom - reservedHeight;
            var elementWidth = Math.Max(60f / scale, body.Width - 6f * s);
            var elementX = body.X + (body.Width - elementWidth) * 0.5f;

            float currentY = bandTop + (2f * s);

            if (showRunButton)
            {
                _runButtonBounds = new RectangleF(elementX, currentY, elementWidth, buttonHeight);
                _stopButtonBounds = RectangleF.Empty;
                currentY += buttonHeight + buttonSpacing;
            }
            else if (showStopButton)
            {
                _runButtonBounds = RectangleF.Empty;
                _stopButtonBounds = new RectangleF(elementX, currentY, elementWidth, buttonHeight);
                currentY += buttonHeight + buttonSpacing;
            }
            else
            {
                _runButtonBounds = RectangleF.Empty;
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

            if (CommandComponent.IsMotionAction() && !_runButtonBounds.IsEmpty)
            {
                var isAutoSend = CommandComponent.IsAutoSendEnabled();
                var runBg = isAutoSend ? ComponentUiColors.Warning : ComponentUiColors.Active;
                GrasshopperUiDraw.DrawRoundedButton(graphics, _runButtonBounds, scale, runBg, _runMouseDown, _runMouseOver);

                var buttonFont = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size / scale, FontStyle.Bold);
                graphics.DrawString(isAutoSend ? ComponentButtonLabels.AutoSend : ComponentButtonLabels.Run, buttonFont, Brushes.White, _runButtonBounds, GH_TextRenderingConstants.CenterCenter);
                buttonFont.Dispose();
            }

            if (CommandComponent._action == URActionKind.Stop && !_stopButtonBounds.IsEmpty)
            {
                GrasshopperUiDraw.DrawRoundedButton(graphics, _stopButtonBounds, scale, ComponentUiColors.Danger, _stopMouseDown, _stopMouseOver);

                var buttonFont = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size / scale, FontStyle.Bold);
                graphics.DrawString(ComponentButtonLabels.Stop, buttonFont, Brushes.White, _stopButtonBounds, GH_TextRenderingConstants.CenterCenter);
                buttonFont.Dispose();
            }

            // Draw dropdown (below stop button if in Stop mode, otherwise at the top)
            var cornerRadiusDropdown = (int)Math.Max(2, Math.Round(8f / scale));
            var dropdownBg = _dropdownHover ? ComponentUiColors.DropdownHover : ComponentUiColors.Dropdown;
            
            using (var path = GrasshopperUiDraw.RoundedRect(_dropdownBounds, cornerRadiusDropdown))
            {
                graphics.FillPath(new SolidBrush(dropdownBg), path);
                graphics.DrawPath(new Pen(GrasshopperUiDraw.Darken(dropdownBg, 0.3), 1.2f), path);
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
                    var itemBg = i == _hoverItemIndex ? ComponentUiColors.DropdownItemHover : ComponentUiColors.Dropdown;
                    
                    using (var itemPath = GrasshopperUiDraw.RoundedRect(itemBounds, cornerRadiusDropdown))
                    {
                        graphics.FillPath(new SolidBrush(itemBg), itemPath);
                        graphics.DrawPath(new Pen(ComponentUiColors.DropdownItemBorder, 0.8f), itemPath);
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

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseDown(sender, e);

            if (e.Button == MouseButtons.Left)
            {
                if (!_runButtonBounds.IsEmpty && _runButtonBounds.Contains(e.CanvasLocation))
                {
                    _runMouseDown = true;
                    Owner.OnDisplayExpired(false);
                    return GH_ObjectResponse.Capture;
                }

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
                if (_runMouseDown && !_runButtonBounds.IsEmpty && _runButtonBounds.Contains(e.CanvasLocation))
                {
                    _runMouseDown = false;
                    _runMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);

                    if (CommandComponent != null)
                        CommandComponent.TriggerRunFromButton();

                    Owner.OnDisplayExpired(false);
                    return GH_ObjectResponse.Release;
                }
                _runMouseDown = false;

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
            bool wasRunOver = _runMouseOver;
            bool wasStopOver = _stopMouseOver;
            int wasHoverIndex = _hoverItemIndex;

            _dropdownHover = _dropdownBounds.Contains(e.CanvasLocation);
            _runMouseOver = !_runButtonBounds.IsEmpty && _runButtonBounds.Contains(e.CanvasLocation);
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

            if (_dropdownHover != wasDropdownHover || _runMouseOver != wasRunOver || _stopMouseOver != wasStopOver || _hoverItemIndex != wasHoverIndex)
            {
                Owner.OnDisplayExpired(false);
            }

            if (_dropdownHover || _runMouseOver || _stopMouseOver || _hoverItemIndex >= 0)
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
