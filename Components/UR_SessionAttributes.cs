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
    /// Custom UI attributes for UR Session component with a connect/disconnect button
    /// </summary>
    public class UR_SessionAttributes : GH_ComponentAttributes
    {
        public UR_SessionAttributes(UR_SessionComponent owner) : base(owner) 
        { 
            _owner = owner;
        }

        private readonly UR_SessionComponent _owner;
        private RectangleF _buttonBounds;
        private bool _mouseDown;
        private bool _mouseOver;

        // Theme colors matching GrasshopperMCP pattern
        static readonly Color Success = Color.FromArgb(0x10, 0xB9, 0x81); // Green for "Connect"
        static readonly Color Danger = Color.FromArgb(0xEF, 0x44, 0x44);  // Red for "Disconnect"

        protected override void Layout()
        {
            base.Layout();

            var buttonWidth = 120f;
            var buttonHeight = 28f;
            var padding = 8f;

            var body = Bounds;
            Bounds = new RectangleF(body.X, body.Y, body.Width, body.Height + buttonHeight + padding);

            var btnX = body.X + (body.Width - buttonWidth) * 0.5f;
            var btnY = body.Bottom + padding;
            _buttonBounds = new RectangleF(btnX, btnY, buttonWidth, buttonHeight);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            bool isConnected = _owner._session?.IsConnected ?? false;
            string label = isConnected ? "Disconnect" : "Connect";
            
            // Button color based on connection state (like MCP pattern)
            var bg = isConnected ? Danger : Success;
            var hover = Color.FromArgb(
                Math.Min(255, bg.R + 20),
                Math.Min(255, bg.G + 20),
                Math.Min(255, bg.B + 20));

            var fill = _mouseDown ? Darken(bg, 0.2) : _mouseOver ? hover : bg;

            // Draw button
            using (var path = RoundedRect(_buttonBounds, 8))
            {
                using (var brush = new SolidBrush(fill))
                    graphics.FillPath(brush, path);
                graphics.DrawPath(new Pen(Darken(bg, 0.4f), 1.2f), path);
            }

            // Button text with proper typography
            var buttonFont = new Font(GH_FontServer.Standard.FontFamily, GH_FontServer.Standard.Size, FontStyle.Bold);
            var sz = graphics.MeasureString(label, buttonFont);
            var tx = _buttonBounds.X + (_buttonBounds.Width - sz.Width) / 2f;
            var ty = _buttonBounds.Y + (_buttonBounds.Height - sz.Height) / 2f;
            graphics.DrawString(label, buttonFont, Brushes.White, new PointF(tx, ty));
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

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left && _buttonBounds.Contains(e.CanvasLocation))
            {
                _mouseDown = true;
                Owner.OnDisplayExpired(false);
                return GH_ObjectResponse.Capture;
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                bool wasPressed = _mouseDown && _buttonBounds.Contains(e.CanvasLocation);
                _mouseDown = false;
                _mouseOver = false;
                Owner.OnDisplayExpired(false);

                if (wasPressed)
                {
                    // Toggle connection
                    if (_owner._session == null)
                    {
                        _owner._session = new URSession(
                            string.IsNullOrWhiteSpace(_owner._currentIp) ? "127.0.0.1" : _owner._currentIp);
                    }

                    if (_owner._session.IsConnected)
                    {
                        _owner._session.Dispose();
                        _owner._session = null;
                    }
                    else
                    {
                        _owner._session.Connect(_owner._lastTimeoutMs);
                    }

                    Owner.ExpireSolution(true);
                    return GH_ObjectResponse.Release;
                }
            }
            return base.RespondToMouseUp(sender, e);
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
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

            // Top left arc
            path.AddArc(arc, 180, 90);

            // Top right arc
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            // Bottom right arc
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            // Bottom left arc
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }
}
