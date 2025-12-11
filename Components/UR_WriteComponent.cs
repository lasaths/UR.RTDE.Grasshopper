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
        private static List<string> _log = new List<string>();
        private double _stopDecel = 2.0;
        private URSession _lastSession;

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
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Message = $"{_action}";
            
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo))
            {
                da.SetData(0, false);
                da.SetData(1, "No session provided");
                return;
            }

            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, false);
                da.SetData(1, "Session not connected");
                return;
            }

            _lastSession = session;

            try
            {
                bool result = false;
                string message = "ok";

                switch (_action)
                {
                    case URActionKind.MoveJ:
                        // Get joint values - supports tree input (processes all branches sequentially)
                        var jointsParam = Params.Input[1];
                        var jointsData = jointsParam.VolatileData;
                        
                        if (jointsData.PathCount == 0 || jointsData.DataCount == 0)
                        {
                            da.SetData(0, false);
                            da.SetData(1, "No joint data provided");
                            return;
                        }

                        double speed = 1.05, accel = 1.4;
                        da.GetData(2, ref speed);
                        da.GetData(3, ref accel);

                        // Collect all waypoints from tree - each branch should have 6 joint values
                        var waypoints = new List<double[]>();
                        for (int i = 0; i < jointsData.PathCount; i++)
                        {
                            var branch = jointsData.get_Branch(i);
                            if (branch.Count >= 6)
                            {
                                var joints = new double[6];
                                for (int j = 0; j < 6; j++)
                                {
                                    if (branch[j] is global::Grasshopper.Kernel.Types.GH_Number ghNum)
                                        joints[j] = ghNum.Value;
                                    else if (branch[j] is double d)
                                        joints[j] = d;
                                    else
                                    {
                                        da.SetData(0, false);
                                        da.SetData(1, $"Branch {i}: Invalid joint value at index {j}");
                                        return;
                                    }
                                }
                                waypoints.Add(joints);
                            }
                            else if (branch.Count > 0)
                            {
                                da.SetData(0, false);
                                da.SetData(1, $"Branch {i}: Expected 6 joint values, got {branch.Count}");
                                return;
                            }
                        }

                        if (waypoints.Count == 0)
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Each branch must contain exactly 6 joint angles");
                            return;
                        }

                        // Execute all waypoints sequentially in background (always async)
                        Task.Run(() =>
                        {
                            foreach (var wp in waypoints)
                            {
                                session.MoveJ(wp, speed, accel, true); // async=true for non-blocking
                            }
                        });
                        
                        result = true;
                        message = waypoints.Count == 1 
                            ? "MoveJ command sent" 
                            : $"MoveJ trajectory sent ({waypoints.Count} waypoints)";
                        break;

                    case URActionKind.MoveL:
                        // Get pose/plane data - supports tree input
                        var poseParam = Params.Input[1];
                        var planeParam = Params.Input[2];
                        var poseData = poseParam.VolatileData;
                        var planeData = planeParam.VolatileData;

                        double lSpeed = 0.25, lAccel = 1.2;
                        da.GetData(3, ref lSpeed);
                        da.GetData(4, ref lAccel);

                        var poses = new List<double[]>();

                        // Try planes first
                        if (planeData.PathCount > 0 && planeData.DataCount > 0)
                        {
                            for (int i = 0; i < planeData.PathCount; i++)
                            {
                                var branch = planeData.get_Branch(i);
                                foreach (var item in branch)
                                {
                                    if (item is global::Grasshopper.Kernel.Types.GH_Plane ghPlane && ghPlane.Value.IsValid)
                                    {
                                        poses.Add(PoseUtils.PlaneToPose(ghPlane.Value));
                                    }
                                }
                            }
                        }
                        // Otherwise try pose lists
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
                                        if (branch[j] is global::Grasshopper.Kernel.Types.GH_Number ghNum)
                                            pose[j] = ghNum.Value;
                                        else if (branch[j] is double d)
                                            pose[j] = d;
                                    }
                                    poses.Add(pose);
                                }
                            }
                        }

                        if (poses.Count == 0)
                        {
                            da.SetData(0, false);
                            da.SetData(1, "Provide target Plane(s) or pose list(s) [x,y,z,rx,ry,rz]");
                            return;
                        }

                        // Execute all poses sequentially in background (always async)
                        Task.Run(() =>
                        {
                            foreach (var p in poses)
                            {
                                session.MoveL(p, lSpeed, lAccel, true); // async=true for non-blocking
                            }
                        });
                        
                        result = true;
                        message = poses.Count == 1 
                            ? "MoveL command sent" 
                            : $"MoveL trajectory sent ({poses.Count} waypoints)";
                        break;

                    case URActionKind.Stop:
                        double decel = 2.0;
                        da.GetData(1, ref decel);
                        _stopDecel = decel;
                        bool stopJ = session.StopJ(decel);
                        bool stopL = session.StopL(decel);
                        result = stopJ || stopL;
                        var lastError = session.LastError ?? "Unknown error";
                        message = result ? $"Stop sent (decel {decel})" : $"Stop failed: {lastError}";
                        break;

                    case URActionKind.SetDO:
                        int pin = 0; 
                        bool val = false;
                        da.GetData(1, ref pin);
                        da.GetData(2, ref val);
                        result = session.SetStandardDigitalOut(pin, val);
                        message = result ? $"Digital output {pin} set to {val}" : $"SetDO failed: {session.LastError ?? "Unknown error"}";
                        break;

                    default:
                        da.SetData(0, false);
                        da.SetData(1, "Not implemented");
                        return;
                }

                _log.Clear();
                _log.Add($"{DateTime.Now:HH:mm:ss} - {message}");
                
                da.SetData(0, result);
                da.SetData(1, message);
            }
            catch (Exception ex)
            {
                da.SetData(0, false);
                da.SetData(1, ex.Message);
                
                _log.Clear();
                _log.Add($"{DateTime.Now:HH:mm:ss} - Error: {ex.Message}");
            }
        }

        internal void SetAction(int index)
        {
            if (index >= 0 && index < ActionModes.Length)
            {
                _action = (URActionKind)index;
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
            }

            Params.OnParametersChanged();
            ExpireSolution(true);
        }

        internal void TriggerStopFromButton()
        {
            var session = _lastSession;
            var decel = _stopDecel;
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

                RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    AddRuntimeMessage(ok ? GH_RuntimeMessageLevel.Remark : GH_RuntimeMessageLevel.Error, message);
                    _log.Clear();
                    _log.Add($"{DateTime.Now:HH:mm:ss} - {message}");
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
