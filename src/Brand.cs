using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PTZVC
{
    /// <summary>
    /// PTZVC logos, embedded from src\assets by build.ps1. The black artwork is used as a shape and
    /// recolored so it reads on the dark UI. Every method returns null if a logo is missing.
    /// </summary>
    public static class Brand
    {
        public const string Horizontal = "logo-horizontal.png";
        public const string Vertical = "logo-vertical.png";
        public const string Mark = "logo-mark.png";

        static readonly Dictionary<string, Bitmap> originals = new Dictionary<string, Bitmap>();
        static readonly Dictionary<string, Bitmap> rendered = new Dictionary<string, Bitmap>();

        /// <summary>The logo with its transparent margins trimmed, at full resolution.</summary>
        public static Bitmap Load(string name)
        {
            Bitmap b;
            if (originals.TryGetValue(name, out b)) return b;
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    if (s != null)
                        using (Bitmap raw = new Bitmap(s))
                            b = Trim(raw);
            }
            catch { b = null; }
            originals[name] = b;
            return b;
        }

        /// <summary>The logo recolored to a single color and scaled to the given height (cached).</summary>
        public static Bitmap Tinted(string name, Color color, int height)
        {
            string key = name + "|" + color.ToArgb() + "|" + height;
            Bitmap b;
            if (rendered.TryGetValue(key, out b)) return b;
            Bitmap src = Load(name);
            if (src != null && height > 0)
            {
                int width = Math.Max(1, (int)Math.Round(src.Width * (double)height / src.Height));
                b = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                // keep only the alpha of the artwork and paint it in the requested color
                ColorMatrix m = new ColorMatrix(new float[][]
                {
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, 0, 0 },
                    new float[] { 0, 0, 0, color.A / 255f, 0 },
                    new float[] { color.R / 255f, color.G / 255f, color.B / 255f, 0, 1 }
                });
                using (Graphics g = Graphics.FromImage(b))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetColorMatrix(m);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, width, height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            rendered[key] = b;
            return b;
        }

        static Bitmap Trim(Bitmap raw)
        {
            Bitmap argb = raw.Clone(new Rectangle(0, 0, raw.Width, raw.Height), PixelFormat.Format32bppArgb);
            BitmapData d = argb.LockBits(new Rectangle(0, 0, argb.Width, argb.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] px = new byte[d.Stride * d.Height];
            Marshal.Copy(d.Scan0, px, 0, px.Length);
            argb.UnlockBits(d);
            int minX = argb.Width, minY = argb.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < argb.Height; y++)
                for (int x = 0; x < argb.Width; x++)
                    if (px[y * d.Stride + x * 4 + 3] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0) return argb;
            Bitmap trimmed = argb.Clone(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1), PixelFormat.Format32bppArgb);
            argb.Dispose();
            return trimmed;
        }
    }
}
