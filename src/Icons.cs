using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

namespace BatteryBar
{
    // Bluetooth device icons: the SVG files in icons/ (from svgrepo.com),
    // rasterized to black-on-transparent PNGs and embedded as resources.
    // They are tinted at draw time so one set works on light and dark
    // taskbars and in both panel themes. "other" has no SVG: it falls back
    // to the Segoe MDL2 Assets glyph, as do kinds whose PNG is missing.
    static class Icons
    {
        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        static string ResourceName(string kind)
        {
            switch (kind)
            {
                case "headset": return "icon-headset.png";
                case "mouse": return "icon-mouse.png";
                case "keyboard": return "icon-keyboard.png";
                case "controller": return "icon-controller.png";
                case "phone": case "cellphone": return "icon-phone.png";
                case "speaker": return "icon-speaker.png";
                default: return null;
            }
        }

        public static Bitmap Get(string kind)
        {
            Bitmap bmp;
            if (cache.TryGetValue(kind, out bmp)) return bmp;
            bmp = null;
            string name = ResourceName(kind);
            if (name != null)
            {
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                        if (s != null) bmp = new Bitmap(s);
                }
                catch { bmp = null; }
            }
            cache[kind] = bmp;
            return bmp;
        }

        // Draw the icon fitted into rect, in the given color. The PNG is a
        // black silhouette, so only its alpha matters: opaque pixels become
        // the color, edge pixels blend towards it.
        public static void Draw(Graphics g, string kind, Color color, RectangleF rect, Font fallbackFont)
        {
            var bmp = Get(kind);
            if (bmp == null)
            {
                using (var b = new SolidBrush(color))
                using (var sf = new StringFormat(StringFormatFlags.NoWrap))
                {
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center; sf.Trimming = StringTrimming.EllipsisCharacter;
                    g.DrawString(Kinds.Glyph(kind), fallbackFont, b, rect, sf);
                }
                return;
            }
            float scale = Math.Min(rect.Width / bmp.Width, rect.Height / bmp.Height);
            float w = bmp.Width * scale, h = bmp.Height * scale;
            float x = rect.X + (rect.Width - w) / 2, y = rect.Y + (rect.Height - h) / 2;
            var old = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            try
            {
                using (var ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(Tint(color));
                    // (The ImageAttributes overload takes an integer dest rect.)
                    g.DrawImage(bmp, new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(w), (int)Math.Round(h)), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
                }
            }
            finally { g.InterpolationMode = old; }
        }

        static ColorMatrix Tint(Color c)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            return new ColorMatrix(new float[][] {
                new float[] { 0, 0, 0, r, 0 },
                new float[] { 0, 0, 0, g, 0 },
                new float[] { 0, 0, 0, b, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0, 0, 0, 0, 1 } });
        }
    }
}
