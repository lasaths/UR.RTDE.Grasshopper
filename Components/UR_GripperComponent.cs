using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Parameters;
using Rhino;

namespace UR.RTDE.Grasshopper
{
    public class UR_GripperComponent : GH_Component
    {
        private readonly object _stateLock = new object();
        private readonly object _sessionLock = new object();
        private RobotiqBackend _backend = RobotiqBackend.Native;
        private URSession _lastSession;
        private bool _isActivated;
        private bool _isOpen = true; // true = open, false = closed
        private bool _isBusy;
        private bool _lastOk;
        private string _lastMessage = "No session";
        private double _cachedPosition;
        private double _cachedSpeed = 128.0;
        private double _cachedForce = 128.0;
        private bool _positionInitialized;
        private double _lastPosition;
        private bool _hasQueuedMove;
        private MoveRequest _queuedMove;

        public bool IsActivated
        {
            get
            {
                lock (_stateLock)
                    return _isActivated;
            }
        }

        public bool IsOpen
        {
            get
            {
                lock (_stateLock)
                    return _isOpen;
            }
        }

        private readonly struct MoveRequest
        {
            public MoveRequest(double position, double speed, double force)
            {
                Position = position;
                Speed = speed;
                Force = force;
            }

            public double Position { get; }
            public double Speed { get; }
            public double Force { get; }
        }

        public UR_GripperComponent()
          : base("UR Robotiq Gripper", "URGripper",
            "Control a Robotiq gripper via UR.RTDE. Use buttons to Activate and Open/Close, or drive Position directly.",
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
            URSessionGoo goo = null;
            bool hasSession = da.GetData(0, ref goo);
            var session = goo?.Value;
            double position = 0, speed = 128, force = 128;
            da.GetData(1, ref position);
            da.GetData(2, ref speed);
            da.GetData(3, ref force);

            position = ClampInput(position);
            speed = ClampInput(speed);
            force = ClampInput(force);

            CacheInputs(position, speed, force);

            if (!hasSession || session == null || !session.IsConnected)
            {
                ClearRememberedSession();
                ResetSessionState(hasSession ? "Session not connected" : "No session");
                Message = $"{_backend}";
                WriteOutputs(da);
                return;
            }

            RememberSession(session);
            ProcessPositionChange(session, position, speed, force);

            Message = GetCanvasMessage();
            WriteOutputs(da);
        }

        internal void PerformActivate(URSession session)
        {
            if (!TryGetConnectedSession(session, out session))
            {
                ReportImmediateFailure("Session not connected", GH_RuntimeMessageLevel.Warning);
                return;
            }

            if (!TryBeginCommand("Activating gripper"))
            {
                ReportImmediateFailure("Gripper is busy", GH_RuntimeMessageLevel.Warning);
                return;
            }

            ExpireSolution(true);

            var backend = _backend;
            bool install = backend == RobotiqBackend.RtdeBridge;
            int port = DefaultPort(backend);

            Task.Run(() =>
            {
                bool ok;
                string message;

                try
                {
                    ok = session.RobotiqActivate(backend, autoCalibrate: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out string detail);
                    message = ok ? "Gripper activated" : $"Activation failed: {detail}";
                }
                catch (Exception ex)
                {
                    ok = false;
                    message = $"Activation error: {ex.Message}";
                }

                CompleteCommand(session, ok, message, () => _isActivated = true, emitSuccessRuntimeMessage: true, failureLevel: GH_RuntimeMessageLevel.Warning);
            });
        }

        internal void PerformOpenClose(URSession session, bool open)
        {
            if (!TryGetConnectedSession(session, out session))
            {
                ReportImmediateFailure("Session not connected", GH_RuntimeMessageLevel.Warning);
                return;
            }

            if (!TryBeginCommand(open ? "Opening gripper" : "Closing gripper"))
            {
                ReportImmediateFailure("Gripper is busy", GH_RuntimeMessageLevel.Warning);
                return;
            }

            ExpireSolution(true);

            var backend = _backend;
            var inputs = GetCachedInputs();
            bool install = backend == RobotiqBackend.RtdeBridge;
            int port = DefaultPort(backend);

            Task.Run(() =>
            {
                bool ok;
                string message;

                try
                {
                    if (open)
                    {
                        ok = session.RobotiqOpen(backend, inputs.Speed, inputs.Force, waitForMotion: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out string detail);
                        message = ok ? "Gripper opened" : $"Command failed: {detail}";
                    }
                    else
                    {
                        ok = session.RobotiqClose(backend, inputs.Speed, inputs.Force, waitForMotion: true, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out string detail);
                        message = ok ? "Gripper closed" : $"Command failed: {detail}";
                    }
                }
                catch (Exception ex)
                {
                    ok = false;
                    message = $"Command error: {ex.Message}";
                }

                CompleteCommand(session, ok, message, () =>
                {
                    _isActivated = true;
                    _isOpen = open;
                }, emitSuccessRuntimeMessage: true, failureLevel: GH_RuntimeMessageLevel.Warning);
            });
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
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("robotiq_backend")) _backend = (RobotiqBackend)reader.GetInt32("robotiq_backend");
            ClearRememberedSession();
            ResetSessionState("No session");
            return base.Read(reader);
        }

        internal bool TryGetConnectedSession(out URSession session)
        {
            lock (_sessionLock)
            {
                session = _lastSession;
                return session != null && session.IsConnected;
            }
        }

        private static double ClampInput(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            return Math.Max(0.0, Math.Min(255.0, value));
        }

        private void CacheInputs(double position, double speed, double force)
        {
            lock (_stateLock)
            {
                _cachedPosition = position;
                _cachedSpeed = speed;
                _cachedForce = force;
            }
        }

        private void RememberSession(URSession session)
        {
            lock (_sessionLock)
            {
                if (!ReferenceEquals(_lastSession, session))
                {
                    _lastSession = session;
                    ResetSessionState("Ready");
                }
            }
        }

        private void ClearRememberedSession()
        {
            lock (_sessionLock)
                _lastSession = null;
        }

        private void ResetSessionState(string message)
        {
            lock (_stateLock)
            {
                _isActivated = false;
                _isOpen = true;
                _isBusy = false;
                _lastOk = false;
                _lastMessage = message;
                _positionInitialized = false;
                _hasQueuedMove = false;
                _queuedMove = default;
            }
        }

        private void ProcessPositionChange(URSession session, double position, double speed, double force)
        {
            bool shouldStartMove = false;

            lock (_stateLock)
            {
                if (!_positionInitialized)
                {
                    _positionInitialized = true;
                    _lastPosition = position;
                    if (string.IsNullOrWhiteSpace(_lastMessage) || _lastMessage == "No session" || _lastMessage == "Session not connected")
                        _lastMessage = "Ready";
                    return;
                }

                if (Math.Abs(_lastPosition - position) <= 1e-6)
                    return;

                _lastPosition = position;

                if (_isBusy)
                {
                    _hasQueuedMove = true;
                    _queuedMove = new MoveRequest(position, speed, force);
                    _lastOk = false;
                    _lastMessage = $"Queued move to {position:0}";
                    return;
                }

                _isBusy = true;
                _lastOk = false;
                _lastMessage = $"Moving to {position:0}";
                shouldStartMove = true;
            }

            if (shouldStartMove)
                RunPositionMoveAsync(session, position, speed, force);
        }

        private bool TryGetConnectedSession(URSession candidate, out URSession session)
        {
            if (candidate != null && candidate.IsConnected)
            {
                session = candidate;
                RememberSession(candidate);
                return true;
            }

            return TryGetConnectedSession(out session);
        }

        private bool TryBeginCommand(string message)
        {
            lock (_stateLock)
            {
                if (_isBusy)
                    return false;

                _isBusy = true;
                _lastOk = false;
                _lastMessage = message;
                return true;
            }
        }

        private (double Position, double Speed, double Force) GetCachedInputs()
        {
            lock (_stateLock)
                return (_cachedPosition, _cachedSpeed, _cachedForce);
        }

        private void RunPositionMoveAsync(URSession session, double position, double speed, double force)
        {
            var backend = _backend;
            bool install = backend == RobotiqBackend.RtdeBridge;
            int port = DefaultPort(backend);

            Task.Run(() =>
            {
                bool ok;
                string message;

                try
                {
                    ok = session.RobotiqMove(backend, position, speed, force, waitForMotion: false, timeoutMs: 4000, installBridge: install, verbose: false, port: port, out string detail);
                    message = ok ? $"Moved to position {position:0}" : $"Move failed: {detail}";
                }
                catch (Exception ex)
                {
                    ok = false;
                    message = $"Move error: {ex.Message}";
                }

                CompleteCommand(session, ok, message, () =>
                {
                    _isActivated = true;
                    _isOpen = position < 128.0;
                }, emitSuccessRuntimeMessage: false, failureLevel: GH_RuntimeMessageLevel.Warning);
            });
        }

        private void CompleteCommand(URSession session, bool ok, string message, Action applySuccessState, bool emitSuccessRuntimeMessage, GH_RuntimeMessageLevel failureLevel)
        {
            MoveRequest nextMove = default;
            bool startQueuedMove = false;

            lock (_stateLock)
            {
                _isBusy = false;
                _lastOk = ok;
                _lastMessage = message;

                if (ok)
                    applySuccessState?.Invoke();

                if (ok && _hasQueuedMove && session != null && session.IsConnected)
                {
                    nextMove = _queuedMove;
                    _hasQueuedMove = false;
                    _isBusy = true;
                    _lastOk = false;
                    _lastMessage = $"Moving to {nextMove.Position:0}";
                    startQueuedMove = true;
                }
                else
                {
                    _hasQueuedMove = false;
                }
            }

            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                if (!ok || emitSuccessRuntimeMessage)
                    AddRuntimeMessage(ok ? GH_RuntimeMessageLevel.Remark : failureLevel, message);

                ExpireSolution(false);
            }));

            if (startQueuedMove)
                RunPositionMoveAsync(session, nextMove.Position, nextMove.Speed, nextMove.Force);
        }

        private void ReportImmediateFailure(string message, GH_RuntimeMessageLevel level)
        {
            lock (_stateLock)
            {
                _lastOk = false;
                _lastMessage = message;
            }

            AddRuntimeMessage(level, message);
            ExpireSolution(false);
        }

        private string GetCanvasMessage()
        {
            lock (_stateLock)
                return _isBusy ? $"{_backend}..." : $"{_backend}";
        }

        private void WriteOutputs(IGH_DataAccess da)
        {
            bool ok;
            string message;
            bool activated;
            bool isOpen;

            lock (_stateLock)
            {
                ok = !_isBusy && _lastOk;
                message = _lastMessage;
                activated = _isActivated;
                isOpen = _isOpen;
            }

            da.SetData(0, ok);
            da.SetData(1, message);
            da.SetData(2, activated);
            da.SetData(3, isOpen);
        }

        private static int DefaultPort(RobotiqBackend backend)
        {
            return backend switch
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
                    if (GripperComponent.TryGetConnectedSession(out var session))
                        GripperComponent.PerformActivate(session);
                    else
                    {
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
                        Owner.ExpireSolution(false);
                    }
                    return GH_ObjectResponse.Release;
                }

                if (openPressed)
                {
                    _activateMouseOver = false;
                    _openMouseOver = false;
                    _closeMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    if (GripperComponent.TryGetConnectedSession(out var session))
                        GripperComponent.PerformOpenClose(session, true);
                    else
                    {
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
                        Owner.ExpireSolution(false);
                    }
                    return GH_ObjectResponse.Release;
                }

                if (closePressed)
                {
                    _activateMouseOver = false;
                    _openMouseOver = false;
                    _closeMouseOver = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    if (GripperComponent.TryGetConnectedSession(out var session))
                        GripperComponent.PerformOpenClose(session, false);
                    else
                    {
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Session not connected");
                        Owner.ExpireSolution(false);
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
