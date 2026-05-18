using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;

namespace UR.RTDE.Grasshopper
{
    public enum URStreamKind { SpeedJ, ServoJ }

    public class UR_StreamComponent : GH_Component
    {
        internal URStreamKind _kind = URStreamKind.ServoJ;
        internal bool _live;
        internal int _minIntervalMs = 50;

        private URSession _currentSession;
        private DateTime _lastSendUtc = DateTime.MinValue;
        private string _lastSignature = string.Empty;

        internal static readonly string[] StreamModes = { "SpeedJ", "ServoJ" };

        public UR_StreamComponent()
          : base("UR Stream", "URStream",
            "Continuous RTDE streaming via SpeedJ (joint velocities) or ServoJ (joint positions). " +
            "Go Live before sending; use UR Write for single moves. " +
            "Live state is not saved with the document.",
            "UR", "RTDE")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        public override void CreateAttributes()
        {
            m_attributes = new UR_StreamAttributes(this);
        }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddParameter(new URSessionParam(), "Session", "S", "UR RTDE session handle.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("OK", "O", "True if the last stream command succeeded.", GH_ParamAccess.item);
            p.AddTextParameter("Message", "M", "Status or error message.", GH_ParamAccess.item);
            p.AddBooleanParameter("Live", "L", "True when the stream is live (sending).", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            Message = $"{_kind}{(_live ? " | live" : " | off")} | {_minIntervalMs}ms";

            URSessionGoo goo = null;
            if (!da.GetData(0, ref goo))
            {
                da.SetData(0, false);
                da.SetData(1, "No session");
                da.SetData(2, false);
                return;
            }

            var session = goo?.Value;
            _currentSession = session;

            if (session == null || !session.IsConnected)
            {
                TryStopStream(session);
                da.SetData(0, false);
                da.SetData(1, "Session not connected");
                da.SetData(2, false);
                return;
            }

            if (!_live)
            {
                da.SetData(0, true);
                da.SetData(1, "Off. Press Go Live to stream.");
                da.SetData(2, false);
                return;
            }

            if (session.IsStreamSendSuppressed)
            {
                da.SetData(0, true);
                da.SetData(1, "Paused while MoveJ/MoveL runs");
                da.SetData(2, true);
                return;
            }

            if (!TryCollectJointValues(da, out var values, out var error))
            {
                da.SetData(0, false);
                da.SetData(1, error);
                da.SetData(2, true);
                return;
            }

            var signature = BuildSignature(values);
            var elapsedMs = (DateTime.UtcNow - _lastSendUtc).TotalMilliseconds;
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal) && elapsedMs < _minIntervalMs)
            {
                da.SetData(0, true);
                da.SetData(1, "ok (rate limited)");
                da.SetData(2, true);
                return;
            }

            bool ok;
            try
            {
                ok = SendStreamCommand(da, session, values);
            }
            catch (Exception ex)
            {
                da.SetData(0, false);
                da.SetData(1, ex.Message);
                da.SetData(2, true);
                return;
            }

            if (ok)
            {
                _lastSendUtc = DateTime.UtcNow;
                _lastSignature = signature;
            }

            da.SetData(0, ok);
            da.SetData(1, ok ? "ok" : session.LastError ?? "Stream command failed");
            da.SetData(2, true);
        }

        internal void SetStreamKind(int index)
        {
            if (index < 0 || index >= StreamModes.Length) return;
            _kind = (URStreamKind)index;
            RebuildInputsForKind();
        }

        internal void ToggleLive()
        {
            _live = !_live;
            if (!_live)
                TryStopStream(_currentSession);
            ExpireSolution(true);
        }

        internal void RebuildInputsForKind()
        {
            if (Params == null) return;

            while (Params.Input.Count > 1)
            {
                var toRemove = Params.Input[1];
                Params.UnregisterInputParameter(toRemove, true);
            }

            Param_Number Num(string name, string nick, string desc, double def, bool optional = true)
            {
                var p = new Param_Number { Name = name, NickName = nick, Description = desc, Access = GH_ParamAccess.item, Optional = optional };
                p.SetPersistentData(def);
                return p;
            }

            switch (_kind)
            {
                case URStreamKind.SpeedJ:
                    Params.RegisterInputParam(new Param_Number
                    {
                        Name = "JointVelocities",
                        NickName = "QD",
                        Description = "Joint velocities qd[6] (rad/s) for SpeedJ",
                        Access = GH_ParamAccess.list,
                        Optional = false
                    });
                    Params.RegisterInputParam(Num("Acceleration", "A", "Acceleration", 0.5));
                    Params.RegisterInputParam(Num("Time", "T", "Time step dt (s)", 0.02));
                    break;

                case URStreamKind.ServoJ:
                    Params.RegisterInputParam(new Param_Number
                    {
                        Name = "Joints",
                        NickName = "Q",
                        Description = "Joint positions q[6] (rad) for ServoJ",
                        Access = GH_ParamAccess.list,
                        Optional = false
                    });
                    Params.RegisterInputParam(Num("Speed", "V", "Speed", 0.5));
                    Params.RegisterInputParam(Num("Acceleration", "A", "Acceleration", 0.5));
                    Params.RegisterInputParam(Num("Time", "T", "Time step dt (s)", 0.02));
                    Params.RegisterInputParam(Num("Lookahead", "L", "Lookahead time (s)", 0.1));
                    Params.RegisterInputParam(Num("Gain", "G", "Servo gain", 300));
                    break;
            }

            Params.OnParametersChanged();
            ExpireSolution(true);
        }

        private bool SendStreamCommand(IGH_DataAccess da, URSession session, double[] values)
        {
            switch (_kind)
            {
                case URStreamKind.SpeedJ:
                {
                    double accel = 0.5;
                    double time = 0.02;
                    da.GetData(2, ref accel);
                    da.GetData(3, ref time);
                    return session.SpeedJ(values, accel, time);
                }
                case URStreamKind.ServoJ:
                {
                    double speed = 0.5, accel = 0.5, time = 0.02, lookahead = 0.1, gain = 300;
                    da.GetData(2, ref speed);
                    da.GetData(3, ref accel);
                    da.GetData(4, ref time);
                    da.GetData(5, ref lookahead);
                    da.GetData(6, ref gain);
                    return session.ServoJ(values, speed, accel, time, lookahead, gain);
                }
                default:
                    return false;
            }
        }

        private bool TryCollectJointValues(IGH_DataAccess da, out double[] values, out string error)
        {
            values = null;
            error = null;

            if (Params.Input.Count < 2)
            {
                error = "Missing joint input";
                return false;
            }

            var data = Params.Input[1].VolatileData;
            if (data.PathCount == 0 || data.DataCount == 0)
            {
                error = "Provide 6 joint values";
                return false;
            }

            var branch = data.get_Branch(0);
            if (branch.Count < 6)
            {
                error = $"Expected 6 values, got {branch.Count}";
                return false;
            }

            values = new double[6];
            for (int i = 0; i < 6; i++)
            {
                if (branch[i] is GH_Number gn)
                    values[i] = gn.Value;
                else
                {
                    error = $"Invalid value at index {i}";
                    return false;
                }
            }
            return true;
        }

        private static string BuildSignature(double[] values)
        {
            return string.Join(",", Array.ConvertAll(values, v => v.ToString("R")));
        }

        private void TryStopStream(URSession session)
        {
            if (session == null || !session.IsConnected) return;
            try
            {
                if (_kind == URStreamKind.SpeedJ)
                    session.SpeedStop();
                else
                    session.ServoStop();
            }
            catch { }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "UR.RTDE.Grasshopper.Resources.Icons.broadcast-duotone.png";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                        return new System.Drawing.Bitmap(stream);
                }
                return null;
            }
        }

        public override Guid ComponentGuid => new Guid("ffbbd903-f1f6-42de-8a58-ccdd7f0e7b04");

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            RebuildInputsForKind();
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            var intervalRoot = Menu_AppendItem(menu, "Min interval");
            void addInterval(string label, int ms)
            {
                Menu_AppendItem(intervalRoot.DropDown, label, (s, e) => { _minIntervalMs = ms; ExpireSolution(true); }, true, _minIntervalMs == ms);
            }
            addInterval("20 ms", 20);
            addInterval("50 ms", 50);
            addInterval("100 ms", 100);
            addInterval("200 ms", 200);
            addInterval("500 ms", 500);
        }

        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("kind", (int)_kind);
            writer.SetInt32("interval", _minIntervalMs);
            return base.Write(writer);
        }

        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            if (reader.ItemExists("kind")) _kind = (URStreamKind)reader.GetInt32("kind");
            if (reader.ItemExists("interval")) _minIntervalMs = reader.GetInt32("interval");
            _live = false;
            return base.Read(reader);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            TryStopStream(_currentSession);
            base.RemovedFromDocument(document);
        }
    }

    public class UR_StreamAttributes : GH_ComponentAttributes
    {
        private RectangleF _liveButtonBounds;
        private RectangleF _dropdownBounds;
        private RectangleF _dropdownButtonBounds;
        private List<RectangleF> _dropdownItemBounds;
        private bool _dropdownOpen;
        private bool _dropdownHover;
        private bool _liveHover;
        private bool _liveMouseDown;
        private int _hoverItemIndex = -1;

        public UR_StreamAttributes(UR_StreamComponent owner) : base(owner) { }

        private UR_StreamComponent StreamComponent => Owner as UR_StreamComponent;

        protected override void Layout()
        {
            base.Layout();
            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;
            var s = 4f / scale;
            var buttonHeight = 28f / scale;
            var dropdownHeight = 22f / scale;
            var buttonSpacing = 6f / scale;

            var body = Bounds;
            var reservedHeight = buttonHeight + buttonSpacing + dropdownHeight + 4f * s;
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var bandTop = body.Bottom - reservedHeight;
            var elementWidth = Math.Max(60f / scale, body.Width - 6f * s);
            var elementX = body.X + (body.Width - elementWidth) * 0.5f;
            var y = bandTop + 2f * s;

            _liveButtonBounds = new RectangleF(elementX, y, elementWidth, buttonHeight);
            y += buttonHeight + buttonSpacing;
            _dropdownBounds = new RectangleF(elementX, y, elementWidth, dropdownHeight);
            _dropdownButtonBounds = new RectangleF(_dropdownBounds.Right - dropdownHeight, _dropdownBounds.Y, dropdownHeight, dropdownHeight);

            _dropdownItemBounds = new List<RectangleF>();
            if (_dropdownOpen)
            {
                for (int i = 0; i < UR_StreamComponent.StreamModes.Length; i++)
                {
                    _dropdownItemBounds.Add(new RectangleF(
                        _dropdownBounds.X,
                        _dropdownBounds.Bottom + i * dropdownHeight,
                        _dropdownBounds.Width,
                        dropdownHeight));
                }
                Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width,
                    Bounds.Height + UR_StreamComponent.StreamModes.Length * dropdownHeight);
            }
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;

            var live = StreamComponent._live;
            var liveBg = GrasshopperUiDraw.ToggleButtonBase(live, ComponentUiColors.Active);
            GrasshopperUiDraw.DrawRoundedButton(graphics, _liveButtonBounds, scale, liveBg, _liveMouseDown, _liveHover);

            var buttonFont = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size / scale, FontStyle.Bold);
            graphics.DrawString(live ? ComponentButtonLabels.Live : ComponentButtonLabels.GoLive, buttonFont, Brushes.White, _liveButtonBounds, GH_TextRenderingConstants.CenterCenter);
            buttonFont.Dispose();

            var font = new Font(GH_FontServer.FamilyStandard, 7f / scale);
            var dropdownBg = _dropdownHover ? ComponentUiColors.DropdownHover : ComponentUiColors.Dropdown;
            graphics.FillRectangle(new SolidBrush(dropdownBg), _dropdownBounds);
            graphics.DrawRectangle(new Pen(ComponentUiColors.DropdownBorder, 1f), Rectangle.Round(_dropdownBounds));
            var textBounds = new RectangleF(_dropdownBounds.X, _dropdownBounds.Y, _dropdownBounds.Width - _dropdownButtonBounds.Width, _dropdownBounds.Height);
            graphics.DrawString(UR_StreamComponent.StreamModes[(int)StreamComponent._kind], font, Brushes.Black, textBounds, GH_TextRenderingConstants.CenterCenter);

            if (_dropdownOpen)
            {
                for (int i = 0; i < _dropdownItemBounds.Count; i++)
                {
                    var itemBounds = _dropdownItemBounds[i];
                    var itemBg = i == _hoverItemIndex ? ComponentUiColors.DropdownItemHover : ComponentUiColors.Dropdown;
                    graphics.FillRectangle(new SolidBrush(itemBg), itemBounds);
                    graphics.DrawRectangle(new Pen(ComponentUiColors.DropdownItemBorder, 0.5f), Rectangle.Round(itemBounds));
                    graphics.DrawString(UR_StreamComponent.StreamModes[i], font, Brushes.Black, itemBounds, GH_TextRenderingConstants.CenterCenter);
                }
            }
            font.Dispose();
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseDown(sender, e);
            if (e.Button == MouseButtons.Left && _liveButtonBounds.Contains(e.CanvasLocation))
            {
                _liveMouseDown = true;
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Capture;
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden) return base.RespondToMouseUp(sender, e);
            if (e.Button == MouseButtons.Left)
            {
                if (_liveMouseDown)
                {
                    _liveMouseDown = false;
                    _liveHover = false;
                    global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
                    if (_liveButtonBounds.Contains(e.CanvasLocation))
                    {
                        StreamComponent.ToggleLive();
                        return GH_ObjectResponse.Release;
                    }
                    return GH_ObjectResponse.Release;
                }

                if (_dropdownBounds.Contains(e.CanvasLocation))
                {
                    _dropdownOpen = !_dropdownOpen;
                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Handled;
                }

                if (_dropdownOpen)
                {
                    for (int i = 0; i < _dropdownItemBounds.Count; i++)
                    {
                        if (_dropdownItemBounds[i].Contains(e.CanvasLocation))
                        {
                            _dropdownOpen = false;
                            StreamComponent.SetStreamKind(i);
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

            bool wasLiveHover = _liveHover;
            bool wasDropdownHover = _dropdownHover;
            int wasHoverIndex = _hoverItemIndex;

            _liveHover = !_liveButtonBounds.IsEmpty && _liveButtonBounds.Contains(e.CanvasLocation);
            _dropdownHover = !_dropdownBounds.IsEmpty && _dropdownBounds.Contains(e.CanvasLocation);
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

            if (_liveHover != wasLiveHover || _dropdownHover != wasDropdownHover || _hoverItemIndex != wasHoverIndex)
                Owner.OnDisplayExpired(false);

            if (_liveHover || _dropdownHover || _hoverItemIndex >= 0)
            {
                sender.Cursor = Cursors.Hand;
                return GH_ObjectResponse.Capture;
            }

            global::Grasshopper.Instances.CursorServer.ResetCursor(sender);
            return GH_ObjectResponse.Release;
        }
    }
}
