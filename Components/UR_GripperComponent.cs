using System;
using System.Drawing;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Parameters;

namespace UR.RTDE.Grasshopper
{
    public class UR_GripperComponent : GH_Component
    {
        private RobotiqBackend _backend = RobotiqBackend.Native;
        private bool _isActivated = false;
        private bool _isOpen = true; // true = open, false = closed

        public bool IsActivated => _isActivated;
        public bool IsOpen => _isOpen;

        public UR_GripperComponent()
          : base("UR Robotiq Gripper", "URGripper",
            "Control a Robotiq gripper via UR.RTDE. Use buttons to Activate and Open/Close.",
            "UR", "RTDE")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        public override void CreateAttributes()
        {
            m_attributes = new UR_GripperAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
            p.AddNumberParameter("Position", "P", "Target position (0-255, 0=open, 255=closed). Used when not using Open/Close buttons.", GH_ParamAccess.item, 0.0);
            p.AddNumberParameter("Speed", "V", "Gripper speed (0-255)", GH_ParamAccess.item, 128.0);
            p.AddNumberParameter("Force", "F", "Gripper force (0-255)", GH_ParamAccess.item, 128.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "O", "True if command succeeded.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Status or error.", GH_ParamAccess.item);
            p.AddBooleanParameter("Activated", "A", "True if gripper is activated.", GH_ParamAccess.item);
            p.AddBooleanParameter("IsOpen", "IO", "Current state: True=Open, False=Closed.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Message = $"{_backend}";
            
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo))
            {
                da.SetData(0, false);
                da.SetData(1, "No session");
                da.SetData(2, _isActivated);
                da.SetData(3, _isOpen);
                return;
            }

            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, false);
                da.SetData(1, "Session not connected");
                da.SetData(2, _isActivated);
                da.SetData(3, _isOpen);
                return;
            }

            // Get position, speed, force
            double position = 0, speed = 128, force = 128;
            da.GetData(1, ref position);
            da.GetData(2, ref speed);
            da.GetData(3, ref force);

            // Clamp values
            position = Math.Max(0, Math.Min(255, position));
            speed = Math.Max(0, Math.Min(255, speed));
            force = Math.Max(0, Math.Min(255, force));

            // Output current state
            da.SetData(0, true);
            da.SetData(1, "Ready");
            da.SetData(2, _isActivated);
            da.SetData(3, _isOpen);
        }

        internal void PerformActivate(URSession session)
        {
            if (session == null || !session.IsConnected) return;

            try
            {
                bool install = _backend == RobotiqBackend.RtdeBridge;
                int port = DefaultPort();
                bool ok = session.RobotiqActivate(_backend, autoCalibrate: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out string msg);
                
                if (ok)
                {
                    _isActivated = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Gripper activated");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Activation failed: {msg}");
                }
                
                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Activation error: {ex.Message}");
            }
        }

        internal void PerformOpenClose(URSession session, bool open)
        {
            if (session == null || !session.IsConnected) return;

            try
            {
                // Get current speed and force from inputs
                double speed = 128, force = 128;
                if (Params.Input.Count > 2)
                {
                    var speedParam = Params.Input[2];
                    var forceParam = Params.Input[3];
                    
                    if (speedParam.SourceCount > 0 && speedParam.VolatileData.DataCount > 0)
                    {
                        var speedGoo = speedParam.VolatileData.get_Branch(0)[0] as global::Grasshopper.Kernel.Types.GH_Number;
                        if (speedGoo != null) speed = speedGoo.Value;
                    }
                    if (forceParam.SourceCount > 0 && forceParam.VolatileData.DataCount > 0)
                    {
                        var forceGoo = forceParam.VolatileData.get_Branch(0)[0] as global::Grasshopper.Kernel.Types.GH_Number;
                        if (forceGoo != null) force = forceGoo.Value;
                    }
                }

                bool install = _backend == RobotiqBackend.RtdeBridge;
                int port = DefaultPort();
                bool ok;
                string msg;

                if (open)
                {
                    ok = session.RobotiqOpen(_backend, speed, force, waitForMotion: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out msg);
                }
                else
                {
                    ok = session.RobotiqClose(_backend, speed, force, waitForMotion: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out msg);
                }

                if (ok)
                {
                    _isOpen = open;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, open ? "Gripper opened" : "Gripper closed");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Command failed: {msg}");
                }

                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Command error: {ex.Message}");
            }
        }

        internal void PerformMove(URSession session, double position, double speed, double force)
        {
            if (session == null || !session.IsConnected) return;

            try
            {
                bool install = _backend == RobotiqBackend.RtdeBridge;
                int port = DefaultPort();
                bool ok = session.RobotiqMove(_backend, position, speed, force, waitForMotion: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out string msg);

                if (ok)
                {
                    _isOpen = position < 128; // Approximate state
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Moved to position {position:F0}");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Move failed: {msg}");
                }

                ExpireSolution(true);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Move error: {ex.Message}");
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.hand-grabbing-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("f9a7bbeb-e482-42f3-9be3-1d60c5132bbf");

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "Backend: Native (63352)", (s, e) => { _backend = RobotiqBackend.Native; ExpireSolution(true); }, true, _backend == RobotiqBackend.Native);
            Menu_AppendItem(menu, "Backend: RTDE bridge", (s, e) => { _backend = RobotiqBackend.RtdeBridge; ExpireSolution(true); }, true, _backend == RobotiqBackend.RtdeBridge);
            Menu_AppendItem(menu, "Backend: URScript (30002)", (s, e) => { _backend = RobotiqBackend.UrScript; ExpireSolution(true); }, true, _backend == RobotiqBackend.UrScript);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("robotiq_backend", (int)_backend);
            writer.SetBoolean("is_activated", _isActivated);
            writer.SetBoolean("is_open", _isOpen);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("robotiq_backend")) _backend = (RobotiqBackend)reader.GetInt32("robotiq_backend");
            if (reader.ItemExists("is_activated")) _isActivated = reader.GetBoolean("is_activated");
            if (reader.ItemExists("is_open")) _isOpen = reader.GetBoolean("is_open");
            return base.Read(reader);
        }

        private int DefaultPort()
        {
            return _backend switch
            {
                RobotiqBackend.UrScript => 30002,
                RobotiqBackend.Native => 63352,
                _ => 0
            };
        }
    }

    public class UR_GripperAttributes : GH_ComponentAttributes
    {
        private RectangleF _activateButtonBounds;
        private RectangleF _openButtonBounds;
        private RectangleF _closeButtonBounds;
        private bool _activateMouseDown;
        private bool _activateMouseOver;
        private bool _openMouseDown;
        private bool _openMouseOver;
        private bool _closeMouseDown;
        private bool _closeMouseOver;

        public UR_GripperAttributes(UR_GripperComponent owner) : base(owner)
        {
        }

        private UR_GripperComponent GripperComponent => Owner as UR_GripperComponent;

        protected override void Layout()
        {
            base.Layout();

            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;
            var s = 4f / scale; // edge and internal spacing
            var buttonHeight = 28f / scale; // Taller buttons
            var buttonSpacing = 6f / scale; // More spacing between buttons

            var body = Bounds;
            var reservedHeight = (buttonHeight * 3) + (buttonSpacing * 2) + (4f * s); // 3 buttons now
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var bandTop = body.Bottom - reservedHeight;
            var btn1Y = bandTop + (2f * s);
            var buttonWidth = Math.Max(60f / scale, body.Width - 6f * s);
            var btnX = body.X + (body.Width - buttonWidth) * 0.5f;
            
            // Activate button
            _activateButtonBounds = new RectangleF(btnX, btn1Y, buttonWidth, buttonHeight);

            // Open button
            var btn2Y = btn1Y + buttonHeight + buttonSpacing;
            _openButtonBounds = new RectangleF(btnX, btn2Y, buttonWidth, buttonHeight);

            // Close button
            var btn3Y = btn2Y + buttonHeight + buttonSpacing;
            _closeButtonBounds = new RectangleF(btnX, btn3Y, buttonWidth, buttonHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel == GH_CanvasChannel.Objects)
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;

                // Activate button
                bool isActivated = GripperComponent.IsActivated;
                string activateLabel = "Activate";
                
                var activateBg = isActivated ? Color.FromArgb(16, 185, 129) : Color.FromArgb(160, 160, 160);
                var activateHover = Color.FromArgb(
                    Math.Min(255, activateBg.R + 20),
                    Math.Min(255, activateBg.G + 20),
                    Math.Min(255, activateBg.B + 20));
                var activateFill = _activateMouseDown ? Darken(activateBg, 0.2) : _activateMouseOver ? activateHover : activateBg;

                var cornerRadius = (int)Math.Max(2, Math.Round(8f / scale));
                using (var path = RoundedRect(_activateButtonBounds, cornerRadius))
                {
                    using (var brush = new SolidBrush(activateFill))
                        graphics.FillPath(brush, path);
                    using (var pen = new Pen(Darken(activateBg, 0.4), 1.2f))
                        graphics.DrawPath(pen, path);
                }

                var std = GH_FontServer.Standard;
                var buttonFont = new Font(std.FontFamily, std.Size / scale, FontStyle.Bold);
                graphics.DrawString(activateLabel, buttonFont, Brushes.White, _activateButtonBounds, GH_TextRenderingConstants.CenterCenter);

                // Open button
                bool isOpen = GripperComponent.IsOpen;
                string openLabel = "Open";
                
                var openBg = isOpen ? Color.FromArgb(16, 185, 129) : Color.FromArgb(160, 160, 160); // Green if open, gray if closed
                var openHover = Color.FromArgb(
                    Math.Min(255, openBg.R + 20),
                    Math.Min(255, openBg.G + 20),
                    Math.Min(255, openBg.B + 20));
                var openFill = _openMouseDown ? Darken(openBg, 0.2) : _openMouseOver ? openHover : openBg;

                using (var path = RoundedRect(_openButtonBounds, cornerRadius))
                {
                    using (var brush = new SolidBrush(openFill))
                        graphics.FillPath(brush, path);
                    using (var pen = new Pen(Darken(openBg, 0.4), 1.2f))
                        graphics.DrawPath(pen, path);
                }

                graphics.DrawString(openLabel, buttonFont, Brushes.White, _openButtonBounds, GH_TextRenderingConstants.CenterCenter);

                // Close button
                string closeLabel = "Close";
                
                var closeBg = !isOpen ? Color.FromArgb(16, 185, 129) : Color.FromArgb(239, 68, 68); // Green if closed, red if open
                var closeHover = Color.FromArgb(
                    Math.Min(255, closeBg.R + 20),
                    Math.Min(255, closeBg.G + 20),
                    Math.Min(255, closeBg.B + 20));
                var closeFill = _closeMouseDown ? Darken(closeBg, 0.2) : _closeMouseOver ? closeHover : closeBg;

                using (var path = RoundedRect(_closeButtonBounds, cornerRadius))
                {
                    using (var brush = new SolidBrush(closeFill))
                        graphics.FillPath(brush, path);
                    using (var pen = new Pen(Darken(closeBg, 0.4), 1.2f))
                        graphics.DrawPath(pen, path);
                }

                graphics.DrawString(closeLabel, buttonFont, Brushes.White, _closeButtonBounds, GH_TextRenderingConstants.CenterCenter);
                buttonFont.Dispose();
            }
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF bounds, int radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            int diameter = radius * 2;
            var size = new Size(diameter, diameter);
            var arc = new RectangleF(bounds.Location, size);

            if (radius == 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

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
                if (_activateButtonBounds.Contains(e.CanvasLocation))
                {
                    _activateMouseDown = true;
                    Owner.OnDisplayExpired(false);
                    return GH_ObjectResponse.Capture;
                }

                if (_openButtonBounds.Contains(e.CanvasLocation))
                {
                    _openMouseDown = true;
                    Owner.OnDisplayExpired(false);
                    return GH_ObjectResponse.Capture;
                }

                if (_closeButtonBounds.Contains(e.CanvasLocation))
                {
                    _closeMouseDown = true;
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
                bool activatePressed = _activateMouseDown && _activateButtonBounds.Contains(e.CanvasLocation);
                bool openPressed = _openMouseDown && _openButtonBounds.Contains(e.CanvasLocation);
                bool closePressed = _closeMouseDown && _closeButtonBounds.Contains(e.CanvasLocation);
                
                _activateMouseDown = false;
                _openMouseDown = false;
                _closeMouseDown = false;
                Owner.OnDisplayExpired(false);

                if (activatePressed)
                {
                    _activateMouseOver = false;
                    _openMouseOver = false;
                    _closeMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    
                    // Get session from first input
                    URSession session = null;
                    if (Owner.Params.Input.Count > 0)
                    {
                        var sessionParam = Owner.Params.Input[0];
                        if (sessionParam.SourceCount > 0 && sessionParam.VolatileData.DataCount > 0)
                        {
                            var goo = sessionParam.VolatileData.get_Branch(0)[0] as URSessionGoo;
                            session = goo?.Value;
                        }
                    }

                    if (session != null && session.IsConnected)
                    {
                        GripperComponent.PerformActivate(session);
                    }
                    else
                    {
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
                        Owner.ExpireSolution(true);
                    }
                    return GH_ObjectResponse.Release;
                }

                if (openPressed)
                {
                    _activateMouseOver = false;
                    _openMouseOver = false;
                    _closeMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    
                    // Get session from first input
                    URSession session = null;
                    if (Owner.Params.Input.Count > 0)
                    {
                        var sessionParam = Owner.Params.Input[0];
                        if (sessionParam.SourceCount > 0 && sessionParam.VolatileData.DataCount > 0)
                        {
                            var goo = sessionParam.VolatileData.get_Branch(0)[0] as URSessionGoo;
                            session = goo?.Value;
                        }
                    }

                    if (session != null && session.IsConnected)
                    {
                        GripperComponent.PerformOpenClose(session, true); // Open = true
                    }
                    else
                    {
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
                        Owner.ExpireSolution(true);
                    }
                    return GH_ObjectResponse.Release;
                }

                if (closePressed)
                {
                    _activateMouseOver = false;
                    _openMouseOver = false;
                    _closeMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    
                    // Get session from first input
                    URSession session = null;
                    if (Owner.Params.Input.Count > 0)
                    {
                        var sessionParam = Owner.Params.Input[0];
                        if (sessionParam.SourceCount > 0 && sessionParam.VolatileData.DataCount > 0)
                        {
                            var goo = sessionParam.VolatileData.get_Branch(0)[0] as URSessionGoo;
                            session = goo?.Value;
                        }
                    }

                    if (session != null && session.IsConnected)
                    {
                        GripperComponent.PerformOpenClose(session, false); // Close = false
                    }
                    else
                    {
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
                        Owner.ExpireSolution(true);
                    }
                    return GH_ObjectResponse.Release;
                }
            }

            return base.RespondToMouseUp(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseMove(sender, e);
            
            bool wasActivateOver = _activateMouseOver;
            bool wasOpenOver = _openMouseOver;
            bool wasCloseOver = _closeMouseOver;

            _activateMouseOver = _activateButtonBounds.Contains(e.CanvasLocation);
            _openMouseOver = _openButtonBounds.Contains(e.CanvasLocation);
            _closeMouseOver = _closeButtonBounds.Contains(e.CanvasLocation);

            if (_activateMouseOver != wasActivateOver || _openMouseOver != wasOpenOver || _closeMouseOver != wasCloseOver)
            {
                Owner.OnDisplayExpired(false);
                
                if (_activateMouseOver || _openMouseOver || _closeMouseOver)
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

            if (_activateMouseOver || _openMouseOver || _closeMouseOver)
                return GH_ObjectResponse.Capture;

            return base.RespondToMouseMove(sender, e);
        }
    }
}
