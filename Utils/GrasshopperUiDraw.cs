using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace UR.RTDE.Grasshopper
{
    /// <summary>Canvas button labels shared across UR components (Title Case).</summary>
    internal static class ComponentButtonLabels
    {
        public const string Connect = "Connect";
        public const string Disconnect = "Disconnect";
        public const string Reconnect = "Reconnect";

        public const string Listen = "Listen";
        public const string Listening = "Listening";

        public const string GoLive = "Go Live";
        public const string Live = "Live";

        public const string Run = "Run";
        public const string AutoSend = "Auto Send";
        public const string Stop = "Stop";

        public const string Activate = "Activate";
        public const string Open = "Open";
        public const string Close = "Close";
    }

    /// <summary>Shared canvas colors for UR component UI (Tailwind-aligned).</summary>
    internal static class ComponentUiColors
    {
        public static readonly Color Active = Color.FromArgb(16, 185, 129);   // #10B981
        public static readonly Color Inactive = Color.FromArgb(160, 160, 160);
        public static readonly Color Danger = Color.FromArgb(239, 68, 68);    // #EF4444
        public static readonly Color Warning = Color.FromArgb(245, 158, 11);  // #F59E0B

        public static readonly Color Dropdown = Color.LightGray;
        public static readonly Color DropdownHover = Color.FromArgb(180, 180, 180);
        public static readonly Color DropdownItemHover = Color.FromArgb(200, 200, 200);
        public static readonly Color DropdownBorder = Color.DarkGray;
        public static readonly Color DropdownItemBorder = Color.Gray;
    }

    internal static class GrasshopperUiDraw
    {
        public static Color Darken(Color c, double amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb(
                c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        public static Color Lighten(Color c, int amount) =>
            Color.FromArgb(
                c.A,
                Math.Min(255, c.R + amount),
                Math.Min(255, c.G + amount),
                Math.Min(255, c.B + amount));

        public static Color ButtonFill(Color baseColor, bool pressed, bool hover)
        {
            if (pressed) return Darken(baseColor, 0.2);
            if (hover) return Lighten(baseColor, 20);
            return baseColor;
        }

        public static Color ToggleButtonBase(bool on, Color onColor) =>
            on ? onColor : ComponentUiColors.Inactive;

        public static int CornerRadius(float scale) =>
            (int)Math.Max(2, Math.Round(8f / scale));

        public static void DrawRoundedButton(Graphics graphics, RectangleF bounds, float scale, Color baseColor, bool pressed, bool hover)
        {
            var fill = ButtonFill(baseColor, pressed, hover);
            var corner = CornerRadius(scale);
            using (var path = RoundedRect(bounds, corner))
            {
                graphics.FillPath(new SolidBrush(fill), path);
                graphics.DrawPath(new Pen(Darken(baseColor, 0.4), 1.2f), path);
            }
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            var arc = new RectangleF(bounds.Location, new SizeF(diameter, diameter));

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
