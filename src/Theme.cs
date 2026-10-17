using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PTZVC
{
    public static class Theme
    {
        public static readonly Color Window = Color.FromArgb(14, 14, 16);
        public static readonly Color Panel = Color.FromArgb(24, 24, 27);
        public static readonly Color BodyTop = Color.FromArgb(44, 44, 48);
        public static readonly Color BodyBottom = Color.FromArgb(26, 26, 29);
        public static readonly Color FaceTop = Color.FromArgb(68, 68, 74);
        public static readonly Color FaceBottom = Color.FromArgb(46, 46, 51);
        public static readonly Color FaceHover = Color.FromArgb(82, 82, 89);
        public static readonly Color FacePressed = Color.FromArgb(30, 30, 33);
        public static readonly Color Edge = Color.FromArgb(84, 84, 92);
        public static readonly Color EdgeDark = Color.FromArgb(8, 8, 9);
        public static readonly Color Text = Color.FromArgb(232, 232, 236);
        public static readonly Color TextDim = Color.FromArgb(148, 148, 156);
        public static readonly Color TextFaint = Color.FromArgb(96, 96, 104);
        public static readonly Color Lit = Color.FromArgb(240, 240, 244);
        public static readonly Color Armed = Color.FromArgb(232, 163, 61);
        public static readonly Color Good = Color.FromArgb(61, 220, 132);
        public static readonly Color Bad = Color.FromArgb(229, 72, 77);

        public static readonly Font Small = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static readonly Font Label = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font Normal = new Font("Segoe UI", 9.5f);
        public static readonly Font Big = new Font("Segoe UI", 12f, FontStyle.Bold);
        public static readonly Font Title = new Font("Segoe UI", 15f, FontStyle.Bold);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark window title bar on Windows 10 (1809+) / 11.</summary>
        public static void DarkTitleBar(Form f)
        {
            if (f.IsHandleCreated) ApplyDarkTitleBar(f);
            f.HandleCreated += delegate { ApplyDarkTitleBar(f); };
        }

        static void ApplyDarkTitleBar(Form f)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
                int caption = 0x00100E0E; // COLORREF (0x00BBGGRR) = Theme.Window, Windows 11 only
                DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
            }
            catch { }
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void Centered(Graphics g, string s, Font f, Color c, RectangleF r)
        {
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (SolidBrush b = new SolidBrush(c))
                g.DrawString(s, f, b, r, sf);
        }

        /// <summary>A raised, glossy button face (remote-control style).</summary>
        public static void Face(Graphics g, GraphicsPath path, RectangleF r, bool hover, bool pressed, Color? glow)
        {
            Color top = pressed ? FacePressed : (hover ? FaceHover : FaceTop);
            Color bottom = pressed ? Color.FromArgb(38, 38, 42) : FaceBottom;
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, r.Top - 1), new PointF(0, r.Bottom + 1), top, bottom))
                g.FillPath(b, path);
            if (glow.HasValue)
                using (SolidBrush gb = new SolidBrush(Color.FromArgb(pressed ? 70 : 46, glow.Value)))
                    g.FillPath(gb, path);
            using (Pen edge = new Pen(glow.HasValue ? Color.FromArgb(200, glow.Value) : (pressed ? EdgeDark : Edge), glow.HasValue ? 1.6f : 1f))
                g.DrawPath(edge, path);
        }
    }

    public enum Glyph { None, Up, Down, Left, Right, Plus, Minus, Near, Far, Camera }

    /// <summary>Remote-control style push button with press-and-hold events.</summary>
    public class RemoteButton : Control
    {
        public Glyph Glyph = Glyph.None;
        public string SubText = "";
        public bool Round;
        public float Radius = 9;
        public Color? LitColor;
        public Font LabelFont = Theme.Big;
        /// <summary>Small indicator dot (e.g. "preset stored here").</summary>
        public bool Dot;

        bool hover, down, visualDown;
        bool lit;

        public event EventHandler Pressed;
        public event EventHandler Released;

        public RemoteButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            Size = new Size(64, 44);
        }

        public bool Lit
        {
            get { return lit; }
            set { if (lit != value) { lit = value; Invalidate(); } }
        }

        public bool IsDown { get { return down; } }

        /// <summary>Show the pressed look without a mouse (keyboard shortcuts).</summary>
        public void SetVisualDown(bool on) { if (visualDown != on) { visualDown = on; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            down = true; Invalidate();
            if (Pressed != null) Pressed(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            Release();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) Release();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled) Release();
            Invalidate();
        }

        void Release()
        {
            if (!down) return;
            down = false; Invalidate();
            if (Released != null) Released(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            RectangleF r = new RectangleF(1.5f, 1.5f, Width - 3, Height - 4);
            // drop shadow for the "raised key" look
            using (GraphicsPath sh = Round ? Ellipse(new RectangleF(r.X, r.Y + 2, r.Width, r.Height)) : Theme.Rounded(new RectangleF(r.X, r.Y + 2, r.Width, r.Height), Radius))
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillPath(sb, sh);
            bool pressed = down || visualDown;
            if (pressed) r.Offset(0, 1.5f);
            using (GraphicsPath path = Round ? Ellipse(r) : Theme.Rounded(r, Radius))
            {
                Color? glow = lit && Enabled ? (LitColor.HasValue ? LitColor : Theme.Lit) : (Color?)null;
                Theme.Face(g, path, r, hover && Enabled, pressed, glow);
            }
            Color fg = Enabled ? ForeColor : Theme.TextFaint;
            if (Dot)
                using (SolidBrush db = new SolidBrush(Enabled ? Color.FromArgb(210, 210, 216) : Theme.TextFaint))
                    g.FillEllipse(db, r.Right - 11, r.Y + 6, 5, 5);
            if (Glyph != Glyph.None)
            {
                RectangleF gr = r;
                if (Text.Length > 0) gr.Height -= 12;
                DrawGlyph(g, Glyph, gr, fg);
                if (Text.Length > 0)
                    Theme.Centered(g, Text, Theme.Small, Enabled ? Theme.TextDim : Theme.TextFaint, new RectangleF(r.X, r.Bottom - 17, r.Width, 14));
            }
            else
            {
                RectangleF tr = r;
                if (SubText.Length > 0) tr.Height -= 10;
                Theme.Centered(g, Text, LabelFont, fg, tr);
                if (SubText.Length > 0)
                    Theme.Centered(g, SubText, Theme.Small, Theme.TextDim, new RectangleF(r.X, r.Bottom - 16, r.Width, 12));
            }
        }

        static GraphicsPath Ellipse(RectangleF r)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddEllipse(r);
            return p;
        }

        public static void DrawGlyph(Graphics g, Glyph glyph, RectangleF r, Color c)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            float s = Math.Max(4, Math.Min(r.Width, r.Height) * 0.22f);
            using (SolidBrush b = new SolidBrush(c))
            using (Pen p = new Pen(c, 2.4f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                switch (glyph)
                {
                    case Glyph.Up: g.FillPolygon(b, new PointF[] { new PointF(cx, cy - s), new PointF(cx + s, cy + s * 0.6f), new PointF(cx - s, cy + s * 0.6f) }); break;
                    case Glyph.Down: g.FillPolygon(b, new PointF[] { new PointF(cx, cy + s), new PointF(cx + s, cy - s * 0.6f), new PointF(cx - s, cy - s * 0.6f) }); break;
                    case Glyph.Left: g.FillPolygon(b, new PointF[] { new PointF(cx - s, cy), new PointF(cx + s * 0.6f, cy - s), new PointF(cx + s * 0.6f, cy + s) }); break;
                    case Glyph.Right: g.FillPolygon(b, new PointF[] { new PointF(cx + s, cy), new PointF(cx - s * 0.6f, cy - s), new PointF(cx - s * 0.6f, cy + s) }); break;
                    case Glyph.Plus: g.DrawLine(p, cx - s, cy, cx + s, cy); g.DrawLine(p, cx, cy - s, cx, cy + s); break;
                    case Glyph.Minus: g.DrawLine(p, cx - s, cy, cx + s, cy); break;
                    case Glyph.Near:
                        // small flower = macro / near
                        for (int i = 0; i < 5; i++)
                        {
                            double a = i * Math.PI * 2 / 5 - Math.PI / 2;
                            g.FillEllipse(b, cx + (float)Math.Cos(a) * s * 0.55f - s * 0.38f, cy + (float)Math.Sin(a) * s * 0.55f - s * 0.38f, s * 0.76f, s * 0.76f);
                        }
                        using (SolidBrush hole = new SolidBrush(Theme.FaceBottom)) g.FillEllipse(hole, cx - s * 0.25f, cy - s * 0.25f, s * 0.5f, s * 0.5f);
                        break;
                    case Glyph.Far:
                        // mountain = far / infinity
                        g.FillPolygon(b, new PointF[] { new PointF(cx - s * 1.2f, cy + s * 0.7f), new PointF(cx - s * 0.3f, cy - s * 0.7f), new PointF(cx + s * 0.2f, cy), new PointF(cx + s * 0.55f, cy - s * 0.35f), new PointF(cx + s * 1.2f, cy + s * 0.7f) });
                        break;
                    case Glyph.Camera:
                        // still camera = snapshot: body with a viewfinder bump and a lens
                        using (GraphicsPath body = Theme.Rounded(new RectangleF(cx - s * 1.35f, cy - s * 0.75f, s * 2.7f, s * 1.85f), s * 0.35f))
                            g.FillPath(b, body);
                        g.FillRectangle(b, cx - s * 0.55f, cy - s * 1.1f, s * 1.1f, s * 0.5f);
                        using (SolidBrush hole = new SolidBrush(Theme.FaceBottom))
                            g.FillEllipse(hole, cx - s * 0.62f, cy - s * 0.47f, s * 1.24f, s * 1.24f);
                        g.FillEllipse(b, cx - s * 0.34f, cy - s * 0.19f, s * 0.68f, s * 0.68f);
                        break;
                }
            }
        }
    }

    /// <summary>Circular 4-way direction pad with a center button, like a PTZ remote.</summary>
    public class DPad : Control
    {
        int panDir, tiltDir;   // current pressed direction (tilt +1 = up)
        bool centerDown, pressing;
        int hoverZone = -1;     // 0 up, 1 right, 2 down, 3 left, 4 center
        int keyPan, keyTilt;    // keyboard-driven highlight

        /// <summary>Raised whenever the pressed direction changes; (0,0) means released.</summary>
        public event Action<int, int> DirectionChanged;
        public event EventHandler CenterClicked;

        public DPad()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Size = new Size(210, 210);
            Cursor = Cursors.Hand;
        }

        public void ShowKeyDirection(int pan, int tilt) { keyPan = pan; keyTilt = tilt; Invalidate(); }

        float R { get { return Math.Min(Width, Height) / 2f - 6; } }
        PointF C { get { return new PointF(Width / 2f, Height / 2f); } }

        int Zone(Point p)
        {
            float dx = p.X - C.X, dy = p.Y - C.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d > R + 4) return -1;
            if (d < R * 0.36) return 4;
            double a = Math.Atan2(dy, dx) * 180 / Math.PI; // 0 = right, 90 = down
            if (a >= -135 && a < -45) return 0;
            if (a >= -45 && a < 45) return 1;
            if (a >= 45 && a < 135) return 2;
            return 3;
        }

        static void ZoneDir(int zone, out int pan, out int tilt)
        {
            pan = zone == 1 ? 1 : (zone == 3 ? -1 : 0);
            tilt = zone == 0 ? 1 : (zone == 2 ? -1 : 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            int z = Zone(e.Location);
            if (z == 4) { centerDown = true; Invalidate(); return; }
            if (z < 0) return;
            pressing = true;
            SetDir(z);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int z = Zone(e.Location);
            if (z != hoverZone) { hoverZone = z; Invalidate(); }
            // sliding the finger/mouse around the ring changes direction while held
            if (pressing && z >= 0 && z < 4) SetDir(z);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverZone = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (centerDown)
            {
                centerDown = false; Invalidate();
                if (Zone(e.Location) == 4 && CenterClicked != null) CenterClicked(this, EventArgs.Empty);
            }
            StopPress();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) { centerDown = false; StopPress(); }
        }

        void SetDir(int zone)
        {
            int p, t;
            ZoneDir(zone, out p, out t);
            if (p == panDir && t == tiltDir) return;
            panDir = p; tiltDir = t; Invalidate();
            if (DirectionChanged != null) DirectionChanged(p, t);
        }

        void StopPress()
        {
            if (!pressing) return;
            pressing = false;
            panDir = tiltDir = 0; Invalidate();
            if (DirectionChanged != null) DirectionChanged(0, 0);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            PointF c = C; float r = R;

            // outer bezel
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                g.FillEllipse(sh, c.X - r - 3, c.Y - r, 2 * r + 6, 2 * r + 7);
            using (LinearGradientBrush bez = new LinearGradientBrush(new PointF(0, c.Y - r - 4), new PointF(0, c.Y + r + 4), Color.FromArgb(90, 90, 96), Color.FromArgb(18, 18, 20)))
                g.FillEllipse(bez, c.X - r - 4, c.Y - r - 4, 2 * r + 8, 2 * r + 8);

            // ring sectors
            for (int z = 0; z < 4; z++)
            {
                int p, t;
                ZoneDir(z, out p, out t);
                bool active = (pressing && p == panDir && t == tiltDir) ||
                              (!pressing && (keyPan != 0 || keyTilt != 0) && ((p != 0 && p == keyPan) || (t != 0 && t == keyTilt)));
                bool hov = hoverZone == z && Enabled;
                using (GraphicsPath sp = new GraphicsPath())
                {
                    float start = -135 + z * 90 + 1.5f;
                    sp.AddArc(c.X - r, c.Y - r, 2 * r, 2 * r, start, 87);
                    float ri = r * 0.40f;
                    sp.AddArc(c.X - ri, c.Y - ri, 2 * ri, 2 * ri, start + 87, -87);
                    sp.CloseFigure();
                    Theme.Face(g, sp, new RectangleF(c.X - r, c.Y - r, 2 * r, 2 * r), hov, active, active ? Theme.Lit : (Color?)null);
                }
                // arrow glyph
                double ang = (-90 + z * 90) * Math.PI / 180;
                float gx = c.X + (float)Math.Cos(ang) * r * 0.70f, gy = c.Y + (float)Math.Sin(ang) * r * 0.70f;
                Glyph gl = z == 0 ? Glyph.Up : z == 1 ? Glyph.Right : z == 2 ? Glyph.Down : Glyph.Left;
                float gs = r * 0.36f;
                RemoteButton.DrawGlyph(g, gl, new RectangleF(gx - gs / 2, gy - gs / 2, gs, gs), Enabled ? (active ? Color.White : Theme.Text) : Theme.TextFaint);
            }

            // center button
            float cr = r * 0.33f;
            RectangleF crr = new RectangleF(c.X - cr, c.Y - cr + (centerDown ? 1.5f : 0), 2 * cr, 2 * cr);
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillEllipse(sh, c.X - cr, c.Y - cr + 2.5f, 2 * cr, 2 * cr);
            using (GraphicsPath cp = new GraphicsPath())
            {
                cp.AddEllipse(crr);
                Theme.Face(g, cp, crr, hoverZone == 4 && Enabled, centerDown, null);
            }
            Theme.Centered(g, "CENTER", Theme.Small, Enabled ? Theme.Text : Theme.TextFaint, crr);
        }
    }

    /// <summary>Flat dark slider.</summary>
    public class DarkSlider : Control
    {
        int min = 0, max = 100, val = 0;
        bool dragging;

        /// <summary>Raised only for user changes (drag, click, wheel).</summary>
        public event EventHandler ValueChanged;
        public event EventHandler DragEnded;

        public DarkSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Size = new Size(200, 26);
            Cursor = Cursors.Hand;
        }

        public bool IsDragging { get { return dragging; } }
        public int Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; } }
        public int Maximum { get { return max; } set { max = Math.Max(min, value); Value = val; } }

        /// <summary>Programmatic set (does not raise ValueChanged).</summary>
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v != val) { val = v; Invalidate(); }
            }
        }

        public double Fraction { get { return max == min ? 0 : (val - min) / (double)(max - min); } }

        float TrackX0 { get { return 10; } }
        float TrackX1 { get { return Width - 10; } }

        void SetFromMouse(int x)
        {
            double f = (x - TrackX0) / Math.Max(1, TrackX1 - TrackX0);
            f = Math.Max(0, Math.Min(1, f));
            int v = (int)Math.Round(min + f * (max - min));
            if (v != val)
            {
                val = v; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            dragging = true;
            SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) SetFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndDrag();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture) EndDrag();
        }

        void EndDrag()
        {
            if (!dragging) return;
            dragging = false; Invalidate();
            if (DragEnded != null) DragEnded(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Enabled) return;
            int step = Math.Max(1, (max - min) / 50);
            int v = Math.Max(min, Math.Min(max, val + Math.Sign(e.Delta) * step));
            if (v == val) return;
            val = v; Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            if (DragEnded != null) DragEnded(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float cy = Height / 2f;
            float x0 = TrackX0, x1 = TrackX1;
            float tx = x0 + (float)Fraction * (x1 - x0);
            using (GraphicsPath tr = Theme.Rounded(new RectangleF(x0 - 3, cy - 3, x1 - x0 + 6, 6), 3))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(10, 10, 11)))
                g.FillPath(b, tr);
            if (tx > x0)
                using (GraphicsPath fill = Theme.Rounded(new RectangleF(x0 - 3, cy - 3, tx - x0 + 6, 6), 3))
                using (SolidBrush b = new SolidBrush(Enabled ? Color.FromArgb(170, 170, 176) : Color.FromArgb(70, 70, 74)))
                    g.FillPath(b, fill);
            float kr = dragging ? 9 : 8;
            using (SolidBrush sh = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                g.FillEllipse(sh, tx - kr, cy - kr + 2, 2 * kr, 2 * kr);
            using (LinearGradientBrush kb = new LinearGradientBrush(new PointF(0, cy - kr), new PointF(0, cy + kr),
                Enabled ? Color.FromArgb(235, 235, 238) : Color.FromArgb(90, 90, 94), Enabled ? Color.FromArgb(150, 150, 156) : Color.FromArgb(60, 60, 64)))
                g.FillEllipse(kb, tx - kr, cy - kr, 2 * kr, 2 * kr);
        }
    }

    /// <summary>On/off pill switch.</summary>
    public class ToggleSwitch : Control
    {
        bool on;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Size = new Size(54, 28);
            Cursor = Cursors.Hand;
        }

        /// <summary>Programmatic set (does not raise CheckedChanged).</summary>
        public bool Checked { get { return on; } set { if (on != value) { on = value; Invalidate(); } } }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Toggle();
        }

        public void Toggle()
        {
            on = !on; Invalidate();
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            RectangleF r = new RectangleF(1, 2, Width - 2, Height - 4);
            using (GraphicsPath p = Theme.Rounded(r, r.Height / 2))
            {
                Color bg = !Enabled ? Color.FromArgb(34, 34, 37) : (on ? Color.FromArgb(205, 205, 210) : Color.FromArgb(12, 12, 13));
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                using (Pen pen = new Pen(Theme.Edge)) g.DrawPath(pen, p);
            }
            float d = r.Height - 6;
            float kx = on ? r.Right - d - 3 : r.X + 3;
            using (SolidBrush k = new SolidBrush(!Enabled ? Color.FromArgb(70, 70, 74) : (on ? Color.FromArgb(22, 22, 24) : Color.FromArgb(150, 150, 156))))
                g.FillEllipse(k, kx, r.Y + 3, d, d);
        }
    }

    /// <summary>Owner-drawn dark ComboBox.</summary>
    public class DarkCombo : ComboBox
    {
        public DarkCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.FromArgb(34, 34, 38);
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            ItemHeight = 22;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x000F) return; // WM_PAINT: repaint the frame and arrow in the dark theme
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                Rectangle r = ClientRectangle;
                int bw = SystemInformation.VerticalScrollBarWidth + 2;
                Rectangle btn = new Rectangle(r.Right - bw, r.Top, bw, r.Height);
                using (SolidBrush b = new SolidBrush(BackColor)) g.FillRectangle(b, btn);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float cx = btn.X + btn.Width / 2f, cy = btn.Y + btn.Height / 2f;
                using (Pen p = new Pen(Enabled ? Theme.TextDim : Theme.TextFaint, 1.6f))
                    g.DrawLines(p, new PointF[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
                g.SmoothingMode = SmoothingMode.None;
                using (Pen edge = new Pen(Focused || DroppedDown ? Color.FromArgb(120, 120, 128) : Theme.Edge))
                    g.DrawRectangle(edge, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? Color.FromArgb(70, 70, 78) : Color.FromArgb(34, 34, 38)))
                e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
                    Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Dark colors for context menus.</summary>
    public class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30, 30, 33); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 30, 33); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 30, 33); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 30, 33); } }
        public override Color MenuBorder { get { return Color.FromArgb(70, 70, 76); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(80, 80, 88); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(60, 60, 66); } }
        public override Color SeparatorDark { get { return Color.FromArgb(60, 60, 66); } }
        public override Color SeparatorLight { get { return Color.FromArgb(30, 30, 33); } }
    }

    public class DarkMenu : ContextMenuStrip
    {
        public DarkMenu()
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            ShowImageMargin = false;
        }

        public ToolStripMenuItem Add(string text, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, onClick);
            item.ForeColor = Theme.Text;
            Items.Add(item);
            return item;
        }
    }
}
