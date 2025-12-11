using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;

namespace UR.RTDE.Grasshopper
{
    public enum URReadKind { Joints, Pose, IO, Modes }

    public class UR_ReadComponent : GH_Component
    {
        internal URReadKind _kind = URReadKind.Joints;
        internal bool _autoListen = false;
        internal int _autoIntervalMs = 100;
        
        private readonly object _sessionLock = new object();
        private URSession _currentSession;
        private System.Threading.Timer _readTimer;
        private readonly object _lock = new object();
        private int _timerRunning = 0;  // For preventing timer overlap
        
        private object _lastReadData;
        private string _lastMessage = "";
        private bool _hasNewData = false;

        internal static readonly string[] ReadModes = { "Joints", "Pose", "IO", "Modes" };

        public UR_ReadComponent()
          : base("UR Read", "URRead",
            "Read values from the robot via RTDE.",
            "UR", "RTDE")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override void CreateAttributes()
        {
            m_attributes = new UR_ReadAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Data", "D", "Read result. Joints: list of 6 numbers. Pose: Plane. IO and Modes: tree with sub-paths.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Message or error.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string autoInfo = _autoListen ? $" | {_autoIntervalMs}ms" : "";
            Message = $"{_kind}{autoInfo}";
            
            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo))
            {
                StopAutoListen();
                return;
            }

            var session = goo?.Value;
            if (session == null || !session.IsConnected)
            {
                StopAutoListen();
                da.SetData(0, null);
                da.SetData(1, "Session not connected");
                return;
            }

            bool sessionChanged;
            lock (_sessionLock)
            {
                sessionChanged = _currentSession != session;
                _currentSession = session;
            }

            if (_autoListen)
            {
                if (_readTimer == null || sessionChanged)
                {
                    StartAutoListen();
                }
            }
            else
            {
                StopAutoListen();
                PerformRead(da);
            }

            if (_autoListen)
            {
                lock (_lock)
                {
                    if (_hasNewData)
                    {
                        OutputData(da, _lastReadData, _lastMessage);
                        _hasNewData = false;
                    }
                    else
                    {
                        OutputData(da, _lastReadData, _lastMessage);
                    }
                }
            }
        }

        internal void SetReadKind(int index)
        {
            if (index >= 0 && index < ReadModes.Length)
            {
                _kind = (URReadKind)index;
                ExpireSolution(true);
            }
        }

        internal void ToggleAutoListen()
        {
            _autoListen = !_autoListen;
            if (!_autoListen)
                StopAutoListen();
            ExpireSolution(true);
        }

        private void PerformRead(IGH_DataAccess da)
        {
            URSession session;
            lock (_sessionLock)
            {
                session = _currentSession;
            }
            
            if (session == null || !session.IsConnected)
            {
                da.SetData(0, null);
                da.SetData(1, "Session not connected");
                return;
            }

            try
            {
                object resultData = null;
                string message = "ok";

                switch (_kind)
                {
                    case URReadKind.Joints:
                        var q = session.GetActualQ();
                        var qTree = new GH_Structure<IGH_Goo>();
                        var qPath = new GH_Path(0);
                        for (int i = 0; i < 6 && i < q.Length; i++)
                            qTree.Append(new GH_Number(q[i]), qPath);
                        resultData = qTree;
                        break;

                    case URReadKind.Pose:
                        var p6 = session.GetActualTCPPose();
                        var plane = PoseUtils.PoseToPlane(p6);
                        resultData = plane;
                        break;

                    case URReadKind.IO:
                        var din = session.GetDigitalInState();
                        var dout = session.GetDigitalOutState();
                        var ai0 = session.GetStandardAnalogInput0();
                        var ai1 = session.GetStandardAnalogInput1();
                        var ao0 = session.GetStandardAnalogOutput0();
                        var ao1 = session.GetStandardAnalogOutput1();

                        var ioTree = new GH_Structure<IGH_Goo>();
                        var pDin = new GH_Path(0);
                        var pDout = new GH_Path(1);
                        var pAnalog = new GH_Path(2);
                        for (int i = 0; i < 18; i++)
                        {
                            bool dinBit = ((din >> i) & 1) == 1;
                            bool doutBit = ((dout >> i) & 1) == 1;
                            ioTree.Append(new GH_Boolean(dinBit), pDin);
                            ioTree.Append(new GH_Boolean(doutBit), pDout);
                        }
                        ioTree.Append(new GH_Number(ai0), pAnalog);
                        ioTree.Append(new GH_Number(ai1), pAnalog);
                        ioTree.Append(new GH_Number(ao0), pAnalog);
                        ioTree.Append(new GH_Number(ao1), pAnalog);
                        resultData = ioTree;
                        break;

                    case URReadKind.Modes:
                        var rmode = session.GetRobotMode();
                        var smode = session.GetSafetyMode();
                        var running = session.IsProgramRunning();

                        var modeTree = new GH_Structure<IGH_Goo>();
                        modeTree.Append(new GH_String($"{MapRobotMode(rmode)} ({rmode})"), new GH_Path(0));
                        modeTree.Append(new GH_String($"{MapSafetyMode(smode)} ({smode})"), new GH_Path(1));
                        modeTree.Append(new GH_Boolean(running), new GH_Path(2));
                        resultData = modeTree;
                        break;

                    default:
                        message = "Not implemented";
                        break;
                }

                OutputData(da, resultData, message);
            }
            catch (Exception ex)
            {
                da.SetData(0, null);
                da.SetData(1, ex.Message);
            }
        }

        private void OutputData(IGH_DataAccess da, object data, string message)
        {
            if (data is GH_Structure<IGH_Goo> tree)
                da.SetDataTree(0, tree);
            else
                da.SetData(0, data);
            da.SetData(1, message);
        }

        private void StartAutoListen()
        {
            StopAutoListen();
            _readTimer = new System.Threading.Timer(OnTimerElapsed, null, 0, _autoIntervalMs);
        }

        private void StopAutoListen()
        {
            if (_readTimer != null)
            {
                _readTimer.Dispose();
                _readTimer = null;
            }
        }

        private void OnTimerElapsed(object state)
        {
            // Prevent overlapping timer callbacks
            if (Interlocked.Exchange(ref _timerRunning, 1) == 1)
                return;

            try
            {
                URSession session;
                lock (_sessionLock)
                {
                    session = _currentSession;
                }
                
                if (session == null || !session.IsConnected)
                    return;

                try
                {
                    object resultData = null;
                    string message = "ok";

                    switch (_kind)
                    {
                        case URReadKind.Joints:
                            var q = session.GetActualQ();
                            var qTree = new GH_Structure<IGH_Goo>();
                            var qPath = new GH_Path(0);
                            for (int i = 0; i < 6 && i < q.Length; i++)
                                qTree.Append(new GH_Number(q[i]), qPath);
                            resultData = qTree;
                            break;

                        case URReadKind.Pose:
                            var p6 = session.GetActualTCPPose();
                            var plane = PoseUtils.PoseToPlane(p6);
                            resultData = plane;
                            break;

                        case URReadKind.IO:
                            var din = session.GetDigitalInState();
                            var dout = session.GetDigitalOutState();
                            var ai0 = session.GetStandardAnalogInput0();
                            var ai1 = session.GetStandardAnalogInput1();
                            var ao0 = session.GetStandardAnalogOutput0();
                            var ao1 = session.GetStandardAnalogOutput1();

                            var ioTree = new GH_Structure<IGH_Goo>();
                            var pDin = new GH_Path(0);
                            var pDout = new GH_Path(1);
                            var pAnalog = new GH_Path(2);
                            for (int i = 0; i < 18; i++)
                            {
                                bool dinBit = ((din >> i) & 1) == 1;
                                bool doutBit = ((dout >> i) & 1) == 1;
                                ioTree.Append(new GH_Boolean(dinBit), pDin);
                                ioTree.Append(new GH_Boolean(doutBit), pDout);
                            }
                            ioTree.Append(new GH_Number(ai0), pAnalog);
                            ioTree.Append(new GH_Number(ai1), pAnalog);
                            ioTree.Append(new GH_Number(ao0), pAnalog);
                            ioTree.Append(new GH_Number(ao1), pAnalog);
                            resultData = ioTree;
                            break;

                        case URReadKind.Modes:
                            var rmode = session.GetRobotMode();
                            var smode = session.GetSafetyMode();
                            var running = session.IsProgramRunning();

                            var modeTree = new GH_Structure<IGH_Goo>();
                            modeTree.Append(new GH_String($"{MapRobotMode(rmode)} ({rmode})"), new GH_Path(0));
                            modeTree.Append(new GH_String($"{MapSafetyMode(smode)} ({smode})"), new GH_Path(1));
                            modeTree.Append(new GH_Boolean(running), new GH_Path(2));
                            resultData = modeTree;
                            break;
                    }

                    lock (_lock)
                    {
                        _lastReadData = resultData;
                        _lastMessage = message;
                        _hasNewData = true;
                    }

                    var doc = OnPingDocument();
                    if (doc != null)
                    {
                        doc.ScheduleSolution(5, d => ExpireSolution(false));
                    }
                }
                catch (Exception ex)
                {
                    lock (_lock)
                    {
                        _lastReadData = null;
                        _lastMessage = ex.Message;
                        _hasNewData = true;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _timerRunning, 0);
            }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.binoculars-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }
        
        public override Guid ComponentGuid => new Guid("5db18069-6306-4b80-957b-f189fc71f8cf");

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            
            var intervalRoot = Menu_AppendItem(menu, "Auto interval");
            void addInterval(string label, int ms)
            {
                Menu_AppendItem(intervalRoot.DropDown, label, (s, e) => 
                { 
                    _autoIntervalMs = ms; 
                    if (_autoListen)
                    {
                        StartAutoListen();
                    }
                    ExpireSolution(true);
                }, true, _autoIntervalMs == ms);
            }
            addInterval("20 ms", 20);
            addInterval("50 ms", 50);
            addInterval("100 ms", 100);
            addInterval("200 ms", 200);
            addInterval("500 ms", 500);
            addInterval("1000 ms", 1000);
        }

        private static string MapRobotMode(int mode)
        {
            return mode switch
            {
                0 => "Disconnected",
                1 => "ConfirmSafety",
                2 => "Booting",
                3 => "PowerOff",
                4 => "PowerOn",
                5 => "Idle",
                6 => "Backdrive",
                7 => "Running",
                8 => "UpdatingFirmware",
                _ => "Unknown"
            };
        }

        private static string MapSafetyMode(int mode)
        {
            return mode switch
            {
                1 => "Normal",
                2 => "Reduced",
                3 => "ProtectiveStop",
                4 => "Recovery",
                5 => "SafeguardStop",
                6 => "SystemEmergencyStop",
                7 => "RobotEmergencyStop",
                8 => "Violation",
                9 => "Fault",
                10 => "AutomaticModeSafeguardStop",
                11 => "ThreePositionEnablingStop",
                _ => "Unknown"
            };
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("kind", (int)_kind);
            writer.SetBoolean("auto", _autoListen);
            writer.SetInt32("interval", _autoIntervalMs);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("kind")) _kind = (URReadKind)reader.GetInt32("kind");
            if (reader.ItemExists("auto")) _autoListen = reader.GetBoolean("auto");
            if (reader.ItemExists("interval")) _autoIntervalMs = reader.GetInt32("interval");
            return base.Read(reader);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopAutoListen();
            base.RemovedFromDocument(document);
        }
    }

    public class UR_ReadAttributes : GH_ComponentAttributes
    {
        private RectangleF _dropdownBounds;
        private RectangleF _dropdownButtonBounds;
        private RectangleF _autoListenButtonBounds;
        private List<RectangleF> _dropdownItemBounds;
        private bool _dropdownOpen = false;
        private bool _dropdownHover = false;
        private bool _autoListenHover = false;
        private bool _autoListenMouseDown = false;
        private int _hoverItemIndex = -1;

        public UR_ReadAttributes(UR_ReadComponent owner) : base(owner)
        {
        }

        private UR_ReadComponent ReadComponent => Owner as UR_ReadComponent;

        protected override void Layout()
        {
            base.Layout();

            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;
            var s = 4f / scale;
            var dropdownHeight = 22f / scale; // Taller dropdown for better spacing
            var buttonHeight = 28f / scale;
            var buttonSpacing = 6f / scale;

            var body = Bounds;
            var reservedHeight = buttonHeight + buttonSpacing + dropdownHeight + (4f * s);
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var bandTop = body.Bottom - reservedHeight;
            var elementWidth = Math.Max(60f / scale, body.Width - 6f * s);
            var elementX = body.X + (body.Width - elementWidth) * 0.5f;

            // Auto-listen button (now first/top)
            var buttonY = bandTop + (2f * s);
            _autoListenButtonBounds = new RectangleF(elementX, buttonY, elementWidth, buttonHeight);

            // Dropdown (now below button)
            var dropdownY = buttonY + buttonHeight + buttonSpacing;
            _dropdownBounds = new RectangleF(elementX, dropdownY, elementWidth, dropdownHeight);
            _dropdownButtonBounds = new RectangleF(_dropdownBounds.Right - dropdownHeight, _dropdownBounds.Y, dropdownHeight, dropdownHeight);

            // Dropdown items (only when open)
            _dropdownItemBounds = new List<RectangleF>();
            if (_dropdownOpen)
            {
                for (int i = 0; i < UR_ReadComponent.ReadModes.Length; i++)
                {
                    _dropdownItemBounds.Add(new RectangleF(
                        _dropdownBounds.X,
                        _dropdownBounds.Bottom + (i * dropdownHeight),
                        _dropdownBounds.Width,
                        dropdownHeight));
                }

                // Expand bounds to capture dropdown clicks
                Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, 
                    Bounds.Height + (UR_ReadComponent.ReadModes.Length * dropdownHeight));
            }
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;

            // Draw auto-listen button (now first/top)
            bool isAutoListen = ReadComponent._autoListen;
            var buttonBg = isAutoListen ? Color.FromArgb(16, 185, 129) : Color.FromArgb(160, 160, 160);
            if (_autoListenMouseDown) buttonBg = Darken(buttonBg, 0.2);
            else if (_autoListenHover) buttonBg = Color.FromArgb(
                Math.Min(255, buttonBg.R + 20),
                Math.Min(255, buttonBg.G + 20),
                Math.Min(255, buttonBg.B + 20));

            var cornerRadius = (int)Math.Max(2, Math.Round(8f / scale));
            using (var path = RoundedRect(_autoListenButtonBounds, cornerRadius))
            {
                graphics.FillPath(new SolidBrush(buttonBg), path);
                graphics.DrawPath(new Pen(Darken(buttonBg, 0.4), 1.2f), path);
            }

            var buttonFont = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size / scale, FontStyle.Bold);
            var buttonText = isAutoListen ? "Listening" : "Listen";
            graphics.DrawString(buttonText, buttonFont, Brushes.White, _autoListenButtonBounds, GH_TextRenderingConstants.CenterCenter);
            buttonFont.Dispose();

            // Draw dropdown (now below button)
            var font = new Font(GH_FontServer.FamilyStandard, 7f / scale, FontStyle.Regular);
            var dropdownBg = _dropdownHover ? Color.FromArgb(180, 180, 180) : Color.LightGray;
            graphics.FillRectangle(new SolidBrush(dropdownBg), _dropdownBounds);
            graphics.DrawRectangle(new Pen(Color.DarkGray, 1f), Rectangle.Round(_dropdownBounds));

            // Text bounds excluding arrow area for centering
            var textBounds = new RectangleF(_dropdownBounds.X, _dropdownBounds.Y, 
                _dropdownBounds.Width - _dropdownButtonBounds.Width, _dropdownBounds.Height);
            var selectedText = UR_ReadComponent.ReadModes[(int)ReadComponent._kind];
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
                    graphics.FillRectangle(new SolidBrush(itemBg), itemBounds);
                    graphics.DrawRectangle(new Pen(Color.Gray, 0.5f), Rectangle.Round(itemBounds));
                    graphics.DrawString(UR_ReadComponent.ReadModes[i], font, Brushes.Black, itemBounds, GH_TextRenderingConstants.CenterCenter);
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
                if (_autoListenButtonBounds.Contains(e.CanvasLocation))
                {
                    _autoListenMouseDown = true;
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
                // Auto-listen button
                if (_autoListenMouseDown && _autoListenButtonBounds.Contains(e.CanvasLocation))
                {
                    _autoListenMouseDown = false;
                    _autoListenHover = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    ReadComponent.ToggleAutoListen();
                    return GH_ObjectResponse.Release;
                }
                _autoListenMouseDown = false;

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
                            ReadComponent.SetReadKind(i);
                            return GH_ObjectResponse.Handled;
                        }
                    }
                    // Click outside dropdown closes it
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
            bool wasAutoListenHover = _autoListenHover;
            int wasHoverIndex = _hoverItemIndex;

            _dropdownHover = _dropdownBounds.Contains(e.CanvasLocation);
            _autoListenHover = _autoListenButtonBounds.Contains(e.CanvasLocation);
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

            if (_dropdownHover != wasDropdownHover || _autoListenHover != wasAutoListenHover || _hoverItemIndex != wasHoverIndex)
            {
                Owner.OnDisplayExpired(false);
            }

            if (_dropdownHover || _autoListenHover || _hoverItemIndex >= 0)
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

