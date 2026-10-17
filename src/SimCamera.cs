using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PTZVC
{
    /// <summary>Displays a SimModel as a 16:9 "camera feed" with simulated focus blur and a HUD.</summary>
    public class SimView : Control
    {
        public SimModel Model;
        public string Caption = "VIRTUAL CAMERA";
        public string Footer = "";

        Bitmap buffer;
        readonly ImageAttributes wrap = new ImageAttributes();
        readonly Font hudFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        public SimView(SimModel model)
        {
            Model = model;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            wrap.SetWrapMode(WrapMode.TileFlipXY);
        }

        public Rectangle FrameRect
        {
            get
            {
                int w = ClientSize.Width, h = ClientSize.Height;
                if (w < 16 || h < 9) return Rectangle.Empty;
                int fw = w, fh = w * 9 / 16;
                if (fh > h) { fh = h; fw = h * 16 / 9; }
                return new Rectangle((w - fw) / 2, (h - fh) / 2, fw, fh);
            }
        }

        /// <summary>Blur radius in output pixels for the current focus error.</summary>
        public static double BlurPixels(double focusUnits, double panDeg, double tiltDeg, double zoomFactor, int frameWidth)
        {
            double ideal = SimModel.FocusUnitsForDistance(SimScene.DepthAt(panDeg, tiltDeg));
            double err = Math.Abs(SimModel.FocusNorm(focusUnits) - SimModel.FocusNorm(ideal));
            double px = err * 45 * (0.6 + 0.4 * Math.Sqrt(zoomFactor)) * frameWidth / 960.0;
            return Math.Min(40, px);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);
            Rectangle fr = FrameRect;
            if (fr.IsEmpty || Model == null) return;

            double p, t, z, f;
            Model.Read(out p, out t, out z, out f);
            DrawFrame(g, fr, p, t, z, f, ref buffer, wrap);
            DrawHud(g, fr, SimModel.PanDeg(p), SimModel.TiltDeg(t), SimModel.ZoomFactor(z), f);
        }

        /// <summary>The camera picture alone (no HUD) at the given size, e.g. for a snapshot.</summary>
        public static Bitmap RenderFrame(SimModel model, int width, int height)
        {
            double p, t, z, f;
            model.Read(out p, out t, out z, out f);
            Bitmap frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            Bitmap scratch = null;
            using (ImageAttributes ia = new ImageAttributes())
            using (Graphics g = Graphics.FromImage(frame))
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                DrawFrame(g, new Rectangle(0, 0, width, height), p, t, z, f, ref scratch, ia);
            }
            if (scratch != null) scratch.Dispose();
            return frame;
        }

        /// <summary>Renders the scene into fr; buffer is reusable scratch space.</summary>
        static void DrawFrame(Graphics g, Rectangle fr, double p, double t, double z, double f, ref Bitmap buffer, ImageAttributes wrap)
        {
            double panDeg = SimModel.PanDeg(p), tiltDeg = SimModel.TiltDeg(t);
            double zf = SimModel.ZoomFactor(z);
            double hfov = SimModel.WideHfov / zf;
            double blur = BlurPixels(f, panDeg, tiltDeg, zf, fr.Width);

            // Defocus: render at reduced resolution and upscale smoothly.
            double scale = blur < 0.6 ? 1 : 1 + blur * 0.6;
            int bw = Math.Max(8, (int)(fr.Width / scale));
            int bh = Math.Max(5, (int)(fr.Height / scale));
            if (buffer == null || buffer.Width != bw || buffer.Height != bh)
            {
                if (buffer != null) buffer.Dispose();
                buffer = new Bitmap(bw, bh, PixelFormat.Format32bppPArgb);
            }
            using (Graphics bg = Graphics.FromImage(buffer))
                SimScene.Render(bg, bw, bh, panDeg, tiltDeg, hfov);

            if (scale == 1)
            {
                g.DrawImageUnscaled(buffer, fr.X, fr.Y);
            }
            else
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(buffer, fr, 0, 0, bw, bh, GraphicsUnit.Pixel, wrap);
            }
        }

        void DrawHud(Graphics g, Rectangle fr, double panDeg, double tiltDeg, double zf, double focus)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // crosshair
            int cx = fr.X + fr.Width / 2, cy = fr.Y + fr.Height / 2;
            using (Pen shadow = new Pen(Color.FromArgb(120, 0, 0, 0), 3f))
            using (Pen p = new Pen(Color.FromArgb(220, 255, 255, 255), 1f))
            {
                foreach (Pen pen in new Pen[] { shadow, p })
                {
                    g.DrawLine(pen, cx - 14, cy, cx - 5, cy);
                    g.DrawLine(pen, cx + 5, cy, cx + 14, cy);
                    g.DrawLine(pen, cx, cy - 14, cx, cy - 5);
                    g.DrawLine(pen, cx, cy + 5, cx, cy + 14);
                }
            }

            bool blink = DateTime.Now.Millisecond < 600;
            Pill(g, fr.X + 10, fr.Y + 10, "   LIVE  ·  " + Caption, false);
            if (blink)
                using (SolidBrush red = new SolidBrush(Color.FromArgb(230, 40, 40)))
                    g.FillEllipse(red, fr.X + 18, fr.Y + 17, 8, 8);

            Pill(g, fr.Right - 10, fr.Y + 10, zf.ToString("0.0") + "x", true);

            double dist = SimModel.FocusNorm(focus) < 0.004 ? double.PositiveInfinity : SimModel.NearestFocusM / SimModel.FocusNorm(focus);
            string fs = (Model.AutoFocus ? "AF" : "MF") + "  " + (double.IsInfinity(dist) ? "∞" : dist.ToString(dist < 10 ? "0.0" : "0") + " m");
            Pill(g, fr.X + 10, fr.Bottom - 34, fs, false);
            Pill(g, fr.Right - 10, fr.Bottom - 34,
                "P " + panDeg.ToString("+0.0;-0.0;0.0") + "°   T " + tiltDeg.ToString("+0.0;-0.0;0.0") + "°", true);
            if (Footer.Length > 0)
                Pill(g, cx - (int)(g.MeasureString(Footer, hudFont).Width / 2) - 9, fr.Bottom - 34, Footer, false);
        }

        void Pill(Graphics g, int x, int y, string text, bool alignRight)
        {
            SizeF sz = g.MeasureString(text, hudFont);
            int w = (int)sz.Width + 18, h = 24;
            if (alignRight) x -= w;
            using (GraphicsPath path = Theme.Rounded(new Rectangle(x, y, w, h), 12))
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillPath(bg, path);
            g.DrawString(text, hudFont, Brushes.White, x + 9, y + (h - sz.Height) / 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (buffer != null) buffer.Dispose();
                hudFont.Dispose();
                wrap.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>The built-in virtual PTZ camera: no hardware needed.</summary>
    public class SimCamera : PtzCamera
    {
        public readonly SimModel Model = new SimModel();
        SimView view;

        public SimCamera()
        {
            Model.DepthProbe = SimScene.DepthAt;
            PanRange = new AxisRange(SimModel.PanMin, SimModel.PanMax, 0, true);
            TiltRange = new AxisRange(SimModel.TiltMin, SimModel.TiltMax, 0, true);
            ZoomRange = new AxisRange(SimModel.ZoomMin, SimModel.ZoomMax, 0, true);
            FocusRange = new AxisRange(SimModel.FocusMin, SimModel.FocusMax, SimModel.FocusMin, true);
            SupportsAutoFocus = true;
        }

        public override string Name { get { return "PTZVC Virtual Camera (simulator)"; } }
        public override string Id { get { return "virtual:studio"; } }
        public override string Kind { get { return "Built-in simulator"; } }

        public override void Connect() { }
        public override void Disconnect() { }

        public override string Format(Axis axis, int v)
        {
            switch (axis)
            {
                case Axis.Pan: return SimModel.PanDeg(v).ToString("+0.0;-0.0;0.0") + "°";
                case Axis.Tilt: return SimModel.TiltDeg(v).ToString("+0.0;-0.0;0.0") + "°";
                case Axis.Zoom: return SimModel.ZoomFactor(v).ToString("0.0") + "x";
                default:
                    double n = SimModel.FocusNorm(v);
                    if (n < 0.004) return "∞";
                    double m = SimModel.NearestFocusM / n;
                    return m.ToString(m < 10 ? "0.0" : "0") + " m";
            }
        }

        /// <summary>Speed 0..1 to degrees/second (2 to 80, exponential for fine control).</summary>
        public static double PanDegPerSec(double speed) { return 2 * Math.Pow(40, Math.Max(0, Math.Min(1, speed))); }

        public override void DrivePanTilt(int panDir, int tiltDir, double speed)
        {
            double d = PanDegPerSec(speed);
            Model.DrivePanTilt(panDir, tiltDir, d, d * 0.8);
        }

        public override void DriveZoom(int dir, double speed)
        {
            Model.DriveZoom(dir, 0.06 * Math.Pow(8, speed));
        }

        public override void DriveFocus(int dir, double speed)
        {
            Model.DriveFocus(dir, 0.04 * Math.Pow(8, speed));
        }

        public override void GoTo(int? pan, int? tilt, int? zoom, int? focus)
        {
            if (pan.HasValue || tilt.HasValue)
            {
                PtzState s = Model.Snapshot();
                Model.SetPanTilt(pan.HasValue ? pan.Value : s.Pan, tilt.HasValue ? tilt.Value : s.Tilt, 90, 70);
            }
            if (zoom.HasValue) Model.SetZoom(zoom.Value);
            if (focus.HasValue) Model.SetFocus(focus.Value);
        }

        public override void SetAutoFocus(bool on) { Model.SetAutoFocus(on); }

        public override PtzState State { get { return Model.Snapshot(); } }

        public override void Tick(double dt)
        {
            Model.Step(dt);
            if (view != null) view.Invalidate();
        }

        public override bool AttachPreview(Control host)
        {
            view = new SimView(Model);
            view.Dock = DockStyle.Fill;
            host.Controls.Add(view);
            return true;
        }

        public override bool CanSnapshot { get { return true; } }

        public override Bitmap Snapshot() { return SimView.RenderFrame(Model, 1920, 1080); }

        public override void DetachPreview()
        {
            if (view == null) return;
            if (view.Parent != null) view.Parent.Controls.Remove(view);
            view.Dispose();
            view = null;
        }
    }
}
