using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PTZVC
{
    /// <summary>
    /// A 360-degree virtual studio drawn with vector graphics in "degree space":
    /// x = pan degrees (right positive), y = -tilt degrees (up is negative y).
    /// The wall band spans tilt -12..+40; ceiling above, floor below.
    /// </summary>
    public static class SimScene
    {
        struct DepthRect
        {
            public float X0, Y0, X1, Y1, D;
            public DepthRect(float x0, float y0, float x1, float y1, float d) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; D = d; }
        }

        const float CeilY = -40f, FloorY = 12f;

        // Front-most first. Distances in meters.
        static readonly DepthRect[] Depths = new DepthRect[]
        {
            new DepthRect(-4, -1, 4, 12, 4f),          // podium
            new DepthRect(-21.5f, -7.5f, -12.5f, 1.5f, 3.5f), // focus chart
            new DepthRect(13f, -13f, 21f, -5f, 6f),      // clock
            new DepthRect(37, -31, 83, 1, 1000f),      // outdoors through the window
            new DepthRect(104, -21, 136, -3, 5f),      // TV
            new DepthRect(150, 4, 210, 12, 3f),        // audience chairs
            new DepthRect(-97, -7, -89, 12, 2.5f),     // plant
            new DepthRect(-140, -30, -100, 10, 4f),    // bookshelf
            new DepthRect(-80, -30, -40, 0, 5f),       // whiteboard
        };

        /// <summary>Subject distance (m) at a pan/tilt direction, used by the simulated autofocus.</summary>
        public static double DepthAt(double panDeg, double tiltDeg)
        {
            double y = -tiltDeg;
            if (y < CeilY) return 4.0;
            if (y > FloorY) return Math.Max(1.2, 6.0 * FloorY / y);
            for (int k = -1; k <= 1; k++)
            {
                double x = panDeg + k * 360;
                foreach (DepthRect r in Depths)
                    if (x >= r.X0 && x <= r.X1 && y >= r.Y0 && y <= r.Y1) return r.D;
            }
            return 6.0;
        }

        // ---- resources --------------------------------------------------------

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static Font F(float size, FontStyle style)
        {
            string key = size + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font("Segoe UI", size, style, GraphicsUnit.World);
                fonts[key] = f;
            }
            return f;
        }

        static readonly StringFormat Center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        static Random BookRng() { return new Random(42); }

        // ---- render -----------------------------------------------------------

        public static void Render(Graphics g, int w, int h, double panDeg, double tiltDeg, double hfov)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

            float s = (float)(w / hfov);
            double vfov = hfov * h / w;
            g.Clear(Color.Black);
            g.ResetTransform();
            g.TranslateTransform(w / 2f, h / 2f);
            g.ScaleTransform(s, s);
            g.TranslateTransform((float)-panDeg, (float)tiltDeg);

            double vx0 = panDeg - hfov / 2, vx1 = panDeg + hfov / 2;
            double vy0 = -tiltDeg - vfov / 2, vy1 = -tiltDeg + vfov / 2;
            View v = new View { X0 = vx0, X1 = vx1, Y0 = vy0, Y1 = vy1, Scale = s };

            for (int k = -1; k <= 1; k++)
            {
                float off = k * 360f;
                if (vx1 < -180 + off || vx0 > 180 + off) continue;
                GraphicsState st = g.Save();
                g.TranslateTransform(off, 0);
                View lv = v; lv.X0 -= off; lv.X1 -= off;
                DrawWorld(g, lv);
                g.Restore(st);
            }
        }

        struct View
        {
            public double X0, X1, Y0, Y1;
            public float Scale;
            public bool Sees(double x0, double x1) { return x1 >= X0 && x0 <= X1; }
            public bool Sees(double x0, double y0, double x1, double y1) { return x1 >= X0 && x0 <= X1 && y1 >= Y0 && y0 <= Y1; }
        }

        static void DrawWorld(Graphics g, View v)
        {
            g.SetClip(new RectangleF(-180, -140, 360, 230), CombineMode.Intersect);
            // ceiling
            if (v.Y0 < CeilY)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, -140), new PointF(0, CeilY), Color.FromArgb(18, 19, 22), Color.FromArgb(44, 46, 52)))
                    g.FillRectangle(b, -210, -140, 420, 140 + CeilY);
                using (Pen p = new Pen(Color.FromArgb(58, 60, 66), 0.08f))
                {
                    for (int y = -130; y < CeilY; y += 8) g.DrawLine(p, -210, y, 210, y);
                    for (int x = -210; x <= 210; x += 10) g.DrawLine(p, x, -140, x, CeilY);
                }
                for (int row = 0; row < 3; row++)
                {
                    float y = -52 - row * 16;
                    for (int x = -195; x <= 195; x += 30)
                    {
                        if (!v.Sees(x - 6, y - 2, x + 6, y + 2)) continue;
                        using (SolidBrush glow = new SolidBrush(Color.FromArgb(40, 255, 244, 220)))
                            g.FillRectangle(glow, x - 6.5f, y - 2.2f, 13f, 4.4f);
                        using (SolidBrush lamp = new SolidBrush(Color.FromArgb(250, 246, 232)))
                            g.FillRectangle(lamp, x - 5, y - 1.4f, 10, 2.8f);
                    }
                }
            }

            // floor
            if (v.Y1 > FloorY)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, FloorY), new PointF(0, 90), Color.FromArgb(92, 66, 46), Color.FromArgb(40, 28, 20)))
                    g.FillRectangle(b, -210, FloorY, 420, 90 - FloorY);
                using (Pen p = new Pen(Color.FromArgb(70, 50, 34), 0.12f))
                {
                    float y = FloorY + 1;
                    for (float gap = 1.2f; y < 90; y += gap, gap *= 1.18f) g.DrawLine(p, -210, y, 210, y);
                    for (int x = -210; x <= 210; x += 6) g.DrawLine(p, x, FloorY, x * 1.8f, 90);
                }
            }

            // walls, section by section
            if (v.Y1 > CeilY && v.Y0 < FloorY)
            {
                Wall(g, -30, 30, Color.FromArgb(28, 40, 66));
                Wall(g, 30, 90, Color.FromArgb(58, 54, 50));
                Wall(g, 90, 150, Color.FromArgb(40, 40, 44));
                Wall(g, 150, 180, Color.FromArgb(52, 48, 44));
                Wall(g, -90, -30, Color.FromArgb(46, 50, 58));
                Wall(g, -150, -90, Color.FromArgb(44, 50, 40));
                Wall(g, -180, -150, Color.FromArgb(52, 48, 44));
                using (Pen corner = new Pen(Color.FromArgb(70, 0, 0, 0), 0.3f))
                    for (int x = -210; x <= 210; x += 60) g.DrawLine(corner, x + 30, CeilY, x + 30, FloorY);
                using (SolidBrush board = new SolidBrush(Color.FromArgb(24, 24, 26)))
                    g.FillRectangle(board, -210, FloorY - 1, 420, 1);

                if (v.Sees(-30, 30)) Stage(g, v);
                if (v.Sees(30, 90)) Window(g);
                if (v.Sees(90, 150)) MediaWall(g, v);
                if (v.Sees(150, 180)) BackWall(g, v, 180);
                if (v.Sees(-180, -150)) BackWall(g, v, -180);
                if (v.Sees(-150, -90)) Library(g, v);
                if (v.Sees(-90, -30)) Whiteboard(g, v);
            }

            // degree ruler along the top of the walls
            if (v.Y0 < CeilY + 3 && v.Y1 > CeilY)
            {
                using (SolidBrush band = new SolidBrush(Color.FromArgb(16, 16, 18)))
                    g.FillRectangle(band, -210, CeilY, 420, 3);
                using (Pen tick = new Pen(Color.FromArgb(150, 150, 150), 0.08f))
                using (SolidBrush txt = new SolidBrush(Color.FromArgb(190, 190, 190)))
                {
                    for (int x = -180; x <= 180; x += 5)
                    {
                        if (!v.Sees(x - 3, x + 3)) continue;
                        g.DrawLine(tick, x, CeilY, x, CeilY + (x % 10 == 0 ? 1.0f : 0.5f));
                        if (x % 10 == 0)
                        {
                            int deg = x == -180 ? 180 : x;
                            Text(g, v, deg + "°", F(1.1f, FontStyle.Regular), txt, x, CeilY + 1.9f);
                        }
                    }
                }
            }
        }

        static void Wall(Graphics g, float x0, float x1, Color c)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, CeilY), new PointF(0, FloorY),
                ColorUtil.Light(c, 0.15f), ColorUtil.Dark(c, 0.25f)))
                g.FillRectangle(b, x0, CeilY, x1 - x0, FloorY - CeilY);
        }

        // ---- sections -----------------------------------------------------------

        static void Stage(Graphics g, View v)
        {
            // backdrop
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(-26, 0), new PointF(26, 0), Color.FromArgb(14, 22, 44), Color.FromArgb(14, 22, 44)))
            {
                ColorBlend cb = new ColorBlend();
                cb.Colors = new Color[] { Color.FromArgb(14, 22, 44), Color.FromArgb(36, 60, 110), Color.FromArgb(14, 22, 44) };
                cb.Positions = new float[] { 0, 0.5f, 1 };
                b.InterpolationColors = cb;
                g.FillRectangle(b, -27, -24, 54, 34);
            }
            using (SolidBrush white = new SolidBrush(Color.FromArgb(235, 238, 245)))
            using (SolidBrush soft = new SolidBrush(Color.FromArgb(150, 170, 210)))
            {
                if (!Logo(g, v, Brand.Horizontal, white.Color, 0, -12.9f, 4.4f))
                    Text(g, v, "PTZVC", F(6.5f, FontStyle.Bold), white, 0, -13.5f);
                Text(g, v, "VIRTUAL PTZ STUDIO", F(1.3f, FontStyle.Regular), soft, 0, -8.6f);
            }

            // podium
            using (LinearGradientBrush wood = new LinearGradientBrush(new PointF(-5, 0), new PointF(5, 0), Color.FromArgb(70, 44, 28), Color.FromArgb(120, 80, 50)))
                g.FillRectangle(wood, -4, -1, 8, 13);
            using (SolidBrush top = new SolidBrush(Color.FromArgb(140, 98, 64)))
                g.FillRectangle(top, -4.6f, -2, 9.2f, 1.1f);
            using (Pen mic = new Pen(Color.FromArgb(30, 30, 30), 0.2f))
                g.DrawLine(mic, 1.2f, -2, 0.4f, -4.6f);
            using (SolidBrush micHead = new SolidBrush(Color.FromArgb(20, 20, 20)))
                g.FillEllipse(micHead, -0.1f, -5.4f, 0.9f, 1.2f);
            using (SolidBrush plate = new SolidBrush(Color.FromArgb(210, 180, 120)))
                if (!Logo(g, v, Brand.Mark, plate.Color, 0, 3.5f, 2.6f))
                    Text(g, v, "PTZVC", F(1.0f, FontStyle.Bold), plate, 0, 3.5f);

            // Siemens star focus chart on an easel
            using (Pen leg = new Pen(Color.FromArgb(60, 45, 30), 0.3f))
            {
                g.DrawLine(leg, -17, 1, -20.5f, 12);
                g.DrawLine(leg, -17, 1, -13.5f, 12);
            }
            g.FillRectangle(Brushes.WhiteSmoke, -21.5f, -7.5f, 9, 9);
            if (v.Sees(-21.5, -7.5, -12.5, 1.5))
            {
                const int spokes = 36;
                for (int i = 0; i < spokes; i += 2)
                    g.FillPie(Brushes.Black, -20.9f, -6.9f, 7.8f, 7.8f, i * 360f / spokes, 360f / spokes);
                using (SolidBrush cap = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    Text(g, v, "FOCUS CHART  ·  3.5 m", F(0.38f, FontStyle.Regular), cap, -17, 1.05f);
            }

            // live wall clock
            ClockFace(g, v, 17, -9, 3.6f);
            using (SolidBrush fine = new SolidBrush(Color.FromArgb(190, 200, 220)))
            {
                Text(g, v, "Zoom in to read this line — PTZVC virtual camera", F(0.3f, FontStyle.Regular), fine, 17, -4.6f);
                Text(g, v, "If you can read this you are near full zoom.", F(0.11f, FontStyle.Regular), fine, 17, -4.15f);
            }
        }

        static void ClockFace(Graphics g, View v, float cx, float cy, float r)
        {
            g.FillEllipse(Brushes.Black, cx - r - 0.3f, cy - r - 0.3f, 2 * r + 0.6f, 2 * r + 0.6f);
            g.FillEllipse(Brushes.WhiteSmoke, cx - r, cy - r, 2 * r, 2 * r);
            if (!v.Sees(cx - r, cy - r, cx + r, cy + r)) return;
            using (Pen t = new Pen(Color.Black, 0.12f))
            {
                for (int i = 0; i < 60; i++)
                {
                    double a = i * Math.PI / 30;
                    float inner = i % 5 == 0 ? r * 0.82f : r * 0.9f;
                    g.DrawLine(t, cx + (float)Math.Sin(a) * inner, cy - (float)Math.Cos(a) * inner,
                        cx + (float)Math.Sin(a) * r * 0.96f, cy - (float)Math.Cos(a) * r * 0.96f);
                }
            }
            DateTime now = DateTime.Now;
            double sec = now.Second + now.Millisecond / 1000.0;
            double min = now.Minute + sec / 60;
            double hr = (now.Hour % 12) + min / 60;
            Hand(g, cx, cy, hr / 12, r * 0.5f, 0.35f, Color.Black);
            Hand(g, cx, cy, min / 60, r * 0.75f, 0.22f, Color.Black);
            Hand(g, cx, cy, sec / 60, r * 0.85f, 0.08f, Color.FromArgb(210, 30, 30));
            g.FillEllipse(Brushes.Black, cx - 0.25f, cy - 0.25f, 0.5f, 0.5f);
        }

        static void Hand(Graphics g, float cx, float cy, double frac, float len, float width, Color c)
        {
            double a = frac * 2 * Math.PI;
            using (Pen p = new Pen(c, width))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, cx, cy, cx + (float)Math.Sin(a) * len, cy - (float)Math.Cos(a) * len);
            }
        }

        static void Window(Graphics g)
        {
            RectangleF win = new RectangleF(37, -31, 46, 32);
            GraphicsState st = g.Save();
            g.SetClip(win);
            using (LinearGradientBrush sky = new LinearGradientBrush(new PointF(0, -31), new PointF(0, 1), Color.FromArgb(24, 70, 130), Color.FromArgb(240, 160, 90)))
                g.FillRectangle(sky, win);
            using (SolidBrush sunGlow = new SolidBrush(Color.FromArgb(70, 255, 220, 140)))
                g.FillEllipse(sunGlow, 64, -20, 10, 10);
            using (SolidBrush sun = new SolidBrush(Color.FromArgb(255, 236, 170)))
                g.FillEllipse(sun, 66.5f, -17.5f, 5, 5);
            using (SolidBrush mtn = new SolidBrush(Color.FromArgb(70, 60, 100)))
                g.FillPolygon(mtn, new PointF[] { new PointF(37, -6), new PointF(44, -14), new PointF(50, -9), new PointF(58, -17), new PointF(66, -8), new PointF(74, -13), new PointF(83, -5), new PointF(83, 1), new PointF(37, 1) });
            Random r = new Random(7);
            using (SolidBrush bld = new SolidBrush(Color.FromArgb(28, 30, 40)))
            using (SolidBrush lit = new SolidBrush(Color.FromArgb(255, 214, 120)))
            {
                float x = 37;
                while (x < 83)
                {
                    float bw = 2.2f + (float)r.NextDouble() * 3f;
                    float bh = 4 + (float)r.NextDouble() * 9f;
                    g.FillRectangle(bld, x, 1 - bh, bw - 0.2f, bh);
                    for (float wy = 1 - bh + 0.6f; wy < 0.2f; wy += 0.9f)
                        for (float wx = x + 0.35f; wx < x + bw - 0.6f; wx += 0.7f)
                            if (r.NextDouble() < 0.45) g.FillRectangle(lit, wx, wy, 0.32f, 0.45f);
                    x += bw;
                }
            }
            g.Restore(st);
            using (Pen frame = new Pen(Color.FromArgb(220, 220, 214), 0.9f))
            {
                g.DrawRectangle(frame, win.X, win.Y, win.Width, win.Height);
                g.DrawLine(frame, 60, -31, 60, 1);
                g.DrawLine(frame, 37, -15, 83, -15);
            }
            using (SolidBrush sill = new SolidBrush(Color.FromArgb(200, 198, 190)))
                g.FillRectangle(sill, 35.5f, 1.2f, 49, 1.2f);
        }

        static void MediaWall(Graphics g, View v)
        {
            // speakers
            foreach (float sx in new float[] { 97, 139 })
            {
                g.FillRectangle(Brushes.Black, sx, -18, 4, 22);
                using (SolidBrush cone = new SolidBrush(Color.FromArgb(50, 50, 54)))
                {
                    g.FillEllipse(cone, sx + 0.8f, -16, 2.4f, 2.4f);
                    g.FillEllipse(cone, sx + 0.4f, -11, 3.2f, 3.2f);
                    g.FillEllipse(cone, sx + 0.4f, -5, 3.2f, 3.2f);
                }
            }
            // TV with SMPTE-style color bars
            g.FillRectangle(Brushes.Black, 103, -22, 34, 20);
            RectangleF scr = new RectangleF(104, -21, 32, 18);
            Color[] bars = { Color.FromArgb(192, 192, 192), Color.FromArgb(192, 192, 0), Color.FromArgb(0, 192, 192), Color.FromArgb(0, 192, 0), Color.FromArgb(192, 0, 192), Color.FromArgb(192, 0, 0), Color.FromArgb(0, 0, 192) };
            float bw = scr.Width / 7f;
            for (int i = 0; i < 7; i++)
            {
                using (SolidBrush b = new SolidBrush(bars[i])) g.FillRectangle(b, scr.X + i * bw, scr.Y, bw + 0.02f, scr.Height * 0.67f);
                using (SolidBrush b = new SolidBrush(bars[6 - i])) g.FillRectangle(b, scr.X + i * bw, scr.Y + scr.Height * 0.67f, bw + 0.02f, scr.Height * 0.08f);
            }
            Color[] low = { Color.FromArgb(0, 33, 76), Color.White, Color.FromArgb(50, 0, 106), Color.FromArgb(19, 19, 19), Color.Black, Color.FromArgb(19, 19, 19), Color.FromArgb(29, 29, 29) };
            float lw = scr.Width / 7f;
            for (int i = 0; i < 7; i++)
                using (SolidBrush b = new SolidBrush(low[i])) g.FillRectangle(b, scr.X + i * lw, scr.Y + scr.Height * 0.75f, lw + 0.02f, scr.Height * 0.25f);
            using (SolidBrush lbl = new SolidBrush(Color.FromArgb(230, 230, 230)))
                Text(g, v, "CH 1  ·  COLOR BARS", F(0.8f, FontStyle.Bold), lbl, 128, -4.4f);
            using (SolidBrush stand = new SolidBrush(Color.FromArgb(22, 22, 24)))
                g.FillRectangle(stand, 106, 6, 28, 6);
        }

        static void BackWall(Graphics g, View v, float c)
        {
            // double door
            using (SolidBrush door = new SolidBrush(Color.FromArgb(86, 64, 46)))
                g.FillRectangle(door, c - 9, -18, 18, 30);
            using (Pen seam = new Pen(Color.FromArgb(40, 28, 20), 0.2f))
            {
                g.DrawRectangle(seam, c - 9, -18, 18, 30);
                g.DrawLine(seam, c, -18, c, 12);
            }
            using (SolidBrush handle = new SolidBrush(Color.FromArgb(200, 190, 160)))
            {
                g.FillRectangle(handle, c - 1.6f, -4, 0.6f, 2.4f);
                g.FillRectangle(handle, c + 1.0f, -4, 0.6f, 2.4f);
            }
            // EXIT sign
            using (SolidBrush green = new SolidBrush(Color.FromArgb(20, 170, 70)))
                g.FillRectangle(green, c - 4, -27, 8, 3.4f);
            using (SolidBrush w = new SolidBrush(Color.White))
                Text(g, v, "EXIT", F(2.2f, FontStyle.Bold), w, c, -25.3f);
            // audience chairs
            using (SolidBrush seat = new SolidBrush(Color.FromArgb(122, 31, 43)))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(96, 22, 34)))
            {
                for (float x = 151; x < 209; x += 4.2f)
                {
                    float cx = x - 180 + c;
                    if (cx > c - 10 && cx < c + 9) continue;
                    g.FillRectangle(back, cx, 4, 3.4f, 4.5f);
                    g.FillRectangle(seat, cx - 0.2f, 8.2f, 3.8f, 1.6f);
                }
            }
        }

        static void Library(Graphics g, View v)
        {
            using (SolidBrush shelf = new SolidBrush(Color.FromArgb(78, 56, 38)))
                g.FillRectangle(shelf, -140, -30, 40, 40);
            using (SolidBrush inner = new SolidBrush(Color.FromArgb(38, 28, 20)))
                for (int row = 0; row < 5; row++) g.FillRectangle(inner, -139, -29 + row * 7.8f, 38, 7.0f);
            if (v.Sees(-140, -30, -100, 10))
            {
                Random r = BookRng();
                Color[] palette = { Color.FromArgb(150, 40, 40), Color.FromArgb(40, 90, 150), Color.FromArgb(200, 170, 70), Color.FromArgb(50, 120, 70), Color.FromArgb(120, 70, 140), Color.FromArgb(210, 210, 200), Color.FromArgb(30, 30, 30) };
                for (int row = 0; row < 5; row++)
                {
                    float y = -29 + row * 7.8f;
                    float x = -138.6f;
                    while (x < -102)
                    {
                        float bw = 0.6f + (float)r.NextDouble() * 1.0f;
                        float bh = 5.2f + (float)r.NextDouble() * 1.6f;
                        using (SolidBrush b = new SolidBrush(palette[r.Next(palette.Length)]))
                            g.FillRectangle(b, x, y + 7.0f - bh, bw - 0.08f, bh);
                        x += bw;
                    }
                }
            }
            // plant
            using (SolidBrush pot = new SolidBrush(Color.FromArgb(150, 90, 60)))
                g.FillPolygon(pot, new PointF[] { new PointF(-96, 6), new PointF(-90, 6), new PointF(-90.8f, 12), new PointF(-95.2f, 12) });
            using (SolidBrush leaf = new SolidBrush(Color.FromArgb(46, 120, 60)))
            using (SolidBrush leaf2 = new SolidBrush(Color.FromArgb(70, 150, 80)))
            {
                g.FillEllipse(leaf, -97, -3, 4, 9);
                g.FillEllipse(leaf2, -94, -6, 3.5f, 11);
                g.FillEllipse(leaf, -92.5f, -2, 4, 8);
            }
        }

        static void Whiteboard(Graphics g, View v)
        {
            using (SolidBrush frame = new SolidBrush(Color.FromArgb(170, 172, 176)))
                g.FillRectangle(frame, -80.6f, -30.6f, 41.2f, 31.2f);
            g.FillRectangle(Brushes.WhiteSmoke, -80, -30, 40, 30);
            if (!v.Sees(-80, -30, -40, 0)) return;
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(30, 60, 140)))
                Text(g, v, "PRESET TARGETS", F(1.8f, FontStyle.Bold), ink, -60, -27.2f);
            using (Pen ring = new Pen(Color.FromArgb(200, 40, 40), 0.18f))
            using (SolidBrush num = new SolidBrush(Color.FromArgb(20, 20, 20)))
            {
                for (int i = 0; i < 9; i++)
                {
                    float cx = -72 + (i % 3) * 12;
                    float cy = -20 + (i / 3) * 7.5f;
                    g.DrawEllipse(ring, cx - 2.6f, cy - 2.6f, 5.2f, 5.2f);
                    g.DrawEllipse(ring, cx - 1.4f, cy - 1.4f, 2.8f, 2.8f);
                    g.DrawLine(ring, cx - 3.2f, cy, cx + 3.2f, cy);
                    g.DrawLine(ring, cx, cy - 3.2f, cx, cy + 3.2f);
                    Text(g, v, (i + 1).ToString(), F(1.3f, FontStyle.Bold), num, cx + 3.6f, cy - 2.2f);
                }
            }
        }

        // ---- text helper ---------------------------------------------------------

        /// <summary>Draws text, or a faint bar when it would be too small to be legible.</summary>
        static void Text(Graphics g, View v, string s, Font f, Brush b, float cx, float cy)
        {
            float px = f.Size * v.Scale;
            if (px < 0.6f) return;
            if (px < 4.5f)
            {
                float w = s.Length * f.Size * 0.5f;
                Color c = b is SolidBrush ? ((SolidBrush)b).Color : Color.Gray;
                using (SolidBrush faint = new SolidBrush(Color.FromArgb(110, c)))
                    g.FillRectangle(faint, cx - w / 2, cy - f.Size * 0.25f, w, f.Size * 0.5f);
                return;
            }
            g.DrawString(s, f, b, cx, cy, Center);
        }

        /// <summary>Draws a brand logo centered on (cx, cy), height in degrees. False if the logo isn't available.</summary>
        static bool Logo(Graphics g, View v, string name, Color color, float cx, float cy, float height)
        {
            Bitmap logo = Brand.Tinted(name, color, 256); // one cached texture; sharp enough up to ~10x zoom
            if (logo == null) return false;
            float w = height * logo.Width / logo.Height;
            if (v.Sees(cx - w / 2, cy - height / 2, cx + w / 2, cy + height / 2))
                g.DrawImage(logo, new RectangleF(cx - w / 2, cy - height / 2, w, height));
            return true;
        }
    }

    static class ColorUtil
    {
        public static Color Light(Color c, float amt)
        {
            return Color.FromArgb(c.A, Lerp(c.R, 255, amt), Lerp(c.G, 255, amt), Lerp(c.B, 255, amt));
        }

        public static Color Dark(Color c, float amt)
        {
            return Color.FromArgb(c.A, Lerp(c.R, 0, amt), Lerp(c.G, 0, amt), Lerp(c.B, 0, amt));
        }

        static int Lerp(int a, int b, float t) { return (int)Math.Round(a + (b - a) * t); }
    }
}
