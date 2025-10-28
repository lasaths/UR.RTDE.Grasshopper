using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace UR.RTDE.Grasshopper
{
    internal static class IconProvider
    {
        private static class Palette
        {
            // Approximated Universal Robots brand blue and neutrals
            // RAL Design 240 70 20 (approx): #82B2C9; RAL 9007 (approx): #8F8F8C
            public static readonly Color URBlue = Color.FromArgb(0x82, 0xB2, 0xC9);   // #82B2C9
            public static readonly Color Dark = Color.FromArgb(0x5A, 0x5C, 0x59);      // #5A5C59
            public static readonly Color Mid = Color.FromArgb(0x8F, 0x8F, 0x8C);       // #8F8F8C (RAL 9007)
            public static readonly Color Light = Color.FromArgb(0xD0, 0xD2, 0xD0);     // #D0D2D0
            public static readonly Color Transparent = Color.Transparent;
        }
        // Try to load an embedded PNG first; if not found, draw a simple fallback.
        // To use Phosphor Icons, add 24x24 PNGs as Embedded Resource with names matching below.
        public static Bitmap Session => LoadByCandidatesOrFallback(new[]
        {
            "Resources.Icons.plug-duotone.png",
            "Resources.Icons.phosphor_plug_duotone_24.png"
        }, DrawPlug);
        public static Bitmap Read => LoadByCandidatesOrFallback(new[]
        {
            "Resources.Icons.eye-duotone.png",
            "Resources.Icons.phosphor_eye_duotone_24.png"
        }, DrawEye);
        public static Bitmap Command => LoadByCandidatesOrFallback(new[]
        {
            "Resources.Icons.play-duotone.png",
            "Resources.Icons.phosphor_play_duotone_24.png"
        }, DrawPlay);

        private static Bitmap LoadByCandidatesOrFallback(string[] resourceNames, Func<Bitmap> fallback)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var resourceName in resourceNames)
                {
                    var fullName = FindResourceName(asm, resourceName);
                    if (fullName == null) continue;
                    using (var s = asm.GetManifestResourceStream(fullName))
                    {
                        if (s == null) continue;
                        using (var img = Image.FromStream(s))
                        {
                            return new Bitmap(img);
                        }
                    }
                }
            }
            catch { }
            return fallback();
        }

        private static string FindResourceName(Assembly asm, string endsWith)
        {
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return null;
        }

        private static Bitmap DrawPlug()
        {
            var bmp = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Palette.Transparent);

                // Duotone base (light fill for body)
                using (var bodyFill = new SolidBrush(Palette.Light))
                using (var bodyOutline = new Pen(Palette.Dark, 2f) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
                using (var prong = new Pen(Palette.Mid, 2f))
                using (var cord = new Pen(Palette.URBlue, 2.2f))
                {
                    var rect = new Rectangle(7, 9, 10, 8);
                    g.FillRectangle(bodyFill, rect);
                    g.DrawRectangle(bodyOutline, rect);
                    // prongs (duotone secondary)
                    g.DrawLine(prong, 10, 7, 10, 9);
                    g.DrawLine(prong, 14, 7, 14, 9);
                    // cord (primary color)
                    g.DrawArc(cord, 5, 14, 14, 8, 20, 140);
                }
            }
            return bmp;
        }

        private static Bitmap DrawEye()
        {
            var bmp = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Palette.Transparent);
                using (var outline = new Pen(Palette.Dark, 2f))
                using (var iris = new SolidBrush(Palette.URBlue))
                using (var shadow = new SolidBrush(Color.FromArgb(80, Palette.Mid)))
                {
                    // Eye outline (secondary)
                    g.DrawEllipse(outline, 3, 7, 18, 10);
                    // Duotone shadow (subtle)
                    g.FillEllipse(shadow, 6, 9, 12, 6);
                    // Iris (primary)
                    g.FillEllipse(iris, 10, 10, 4, 4);
                }
            }
            return bmp;
        }

        private static Bitmap DrawPlay()
        {
            var bmp = new Bitmap(24, 24);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Palette.Transparent);
                // Duotone shadow
                using (var shadow = new SolidBrush(Color.FromArgb(90, Palette.Mid)))
                using (var primary = new SolidBrush(Palette.URBlue))
                {
                    Point[] triShadow = { new Point(9, 7), new Point(19, 12), new Point(9, 17) };
                    Point[] tri = { new Point(8, 6), new Point(18, 12), new Point(8, 18) };
                    g.FillPolygon(shadow, triShadow);
                    g.FillPolygon(primary, tri);
                }
            }
            return bmp;
        }
    }
}
