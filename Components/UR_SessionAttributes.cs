using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.Kernel.Attributes;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.GUI;

namespace UR.RTDE.Grasshopper
{
    /// <summary>
    /// UI attributes for the `UR_SessionComponent`, rendering a connect/disconnect button
    /// and handling mouse interaction.
    /// </summary>
    public class UR_SessionAttributes : GH_ComponentAttributes
    {
        /// <summary>
        /// Initializes attributes for the given component owner.
        /// </summary>
        /// <param name="owner">Component owner.</param>
        public UR_SessionAttributes(UR_SessionComponent owner) : base(owner) 
        { 
            _owner = owner;
        }

        private readonly UR_SessionComponent _owner;
        private RectangleF _buttonBounds;
        private bool _mouseDown;
        private bool _mouseOver;

		static readonly Color Success = Color.FromArgb(0x10, 0xB9, 0x81);
		static readonly Color Danger = Color.FromArgb(0xEF, 0x44, 0x44);

		/// <summary>
		/// Computes layout bounds and reserves space for the connect/disconnect button.
		/// </summary>
        protected override void Layout()
        {
            base.Layout();

            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;
            var s = 4f / scale; // edge and internal spacing
            var buttonHeight = 28f / scale; // Taller button to match other components

            var body = Bounds;
            var reservedHeight = buttonHeight + 4f * s;
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + reservedHeight);
            body = Bounds;

            var bandTop = body.Bottom - reservedHeight;
            var bandCenterY = bandTop + reservedHeight * 0.5f;
            const float visualNudge = -1f; // small upward nudge for perceived centering
            var btnY = bandCenterY - (buttonHeight * 0.5f) + visualNudge;
            var buttonWidth = Math.Max(60f / scale, body.Width - 6f * s); // 3s margins on both sides
            var btnX = body.X + (body.Width - buttonWidth) * 0.5f; // center horizontally
            _buttonBounds = new RectangleF(btnX, btnY, buttonWidth, buttonHeight);
        }

		/// <summary>
		/// Renders the button and its label according to the connection state.
		/// </summary>
		/// <param name="canvas">Grasshopper canvas.</param>
		/// <param name="graphics">GDI+ graphics surface.</param>
		/// <param name="channel">Render channel.</param>
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            if (_owner.Locked || _owner.Hidden) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = GH_GraphicsUtil.UiScale <= 0 ? 1f : GH_GraphicsUtil.UiScale;

            bool isConnected = _owner._session?.IsConnected ?? false;
            string label = isConnected ? "Disconnect" : "Connect";
            
            var bg = isConnected ? Danger : Success;
            var hover = Color.FromArgb(
                Math.Min(255, bg.R + 20),
                Math.Min(255, bg.G + 20),
                Math.Min(255, bg.B + 20));

            var fill = _mouseDown ? Darken(bg, 0.2) : _mouseOver ? hover : bg;

            var corner = (int)Math.Max(2, Math.Round(8f / scale));
            using (var path = RoundedRect(_buttonBounds, corner))
            {
                using (var brush = new SolidBrush(fill))
                    graphics.FillPath(brush, path);
                graphics.DrawPath(new Pen(Darken(bg, 0.4f), 1.2f), path);
            }

            var std = GH_FontServer.Standard;
            var buttonFont = new Font(std.FontFamily, std.Size / scale, FontStyle.Bold);
            var text = label;
            var maxTextWidth = _buttonBounds.Width - (12f / scale);
            var measuredWidth = GH_FontServer.StringWidth(text, buttonFont);
            if (measuredWidth > maxTextWidth)
            {
                const string ellipsis = "…";
                var baseText = text;
                while (baseText.Length > 1)
                {
                    baseText = baseText.Substring(0, baseText.Length - 1);
                    var candidate = baseText + ellipsis;
                    if (GH_FontServer.StringWidth(candidate, buttonFont) <= maxTextWidth)
                    {
                        text = candidate;
                        break;
                    }
                }
            }
            graphics.DrawString(text, buttonFont, Brushes.White, _buttonBounds, GH_TextRenderingConstants.CenterCenter);
        }

		/// <summary>
		/// Returns a darker version of the given color.
		/// </summary>
		/// <param name="c">Base color.</param>
		/// <param name="amount">Darken factor in [0,1].</param>
		/// <returns>Darker color.</returns>
        private static Color Darken(Color c, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

		/// <summary>
		/// Captures left mouse down on the button to prepare for a click.
		/// </summary>
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked || _owner.Hidden) return base.RespondToMouseDown(sender, e);
            if (e.Button == System.Windows.Forms.MouseButtons.Left && _buttonBounds.Contains(e.CanvasLocation))
            {
                _mouseDown = true;
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Capture;
            }
            return base.RespondToMouseDown(sender, e);
        }

		/// <summary>
		/// On mouse up, toggles the connection if the button was pressed.
		/// </summary>
        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked || _owner.Hidden) return base.RespondToMouseUp(sender, e);
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                bool wasPressed = _mouseDown && _buttonBounds.Contains(e.CanvasLocation);
                _mouseDown = false;
                _mouseOver = false;
                Owner.OnDisplayExpired(false);

                if (wasPressed)
                {
                    if (_owner._session == null)
                    {
                        _owner._session = new URSession(
                            string.IsNullOrWhiteSpace(_owner._currentIp) ? "127.0.0.1" : _owner._currentIp);
                    }

                    try
                    {
                        if (_owner._session.IsConnected)
                        {
                            _owner._session.Dispose();
                            _owner._session = null;
                        }
                        else
                        {
                            _owner._session.Connect(_owner._lastTimeoutMs);
                        }
                    }
                    catch (Exception ex)
                    {
                        try { _owner._session?.Dispose(); } catch { }
                        _owner._session = null;
                        Owner.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    }

                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Release;
                }
            }
            return base.RespondToMouseUp(sender, e);
        }

		/// <summary>
		/// Updates the hover state and cursor when moving over the button area.
		/// </summary>
        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (_owner.Locked || _owner.Hidden) return base.RespondToMouseMove(sender, e);
            if (_buttonBounds.Contains(e.CanvasLocation))
            {
                if (!_mouseOver)
                {
                    _mouseOver = true;
                    Owner.OnDisplayExpired(false);
                    sender.Cursor = System.Windows.Forms.Cursors.Hand;
                    return GH_ObjectResponse.Capture;
                }
            }
            else
            {
                if (_mouseOver)
                {
                    _mouseOver = false;
                    Owner.OnDisplayExpired(false);
                    Instances.CursorServer.ResetCursor(sender);
                    return GH_ObjectResponse.Release;
                }
            }

            return base.RespondToMouseMove(sender, e);
        }

		/// <summary>
		/// Creates a rounded rectangle graphics path.
		/// </summary>
		/// <param name="bounds">Rectangle bounds.</param>
		/// <param name="radius">Corner radius in pixels.</param>
		/// <returns>Graphics path of the rounded rectangle.</returns>
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
    }
}
