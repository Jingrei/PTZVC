using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PTZVC
{
    /// <summary>The physical-remote body: rounded dark-grey shell with painted labels.</summary>
    public class RemotePanel : Panel
    {
        public class PaintText
        {
            public string Text; public RectangleF Rect; public Font Font; public Color Color; public StringAlignment Align;
        }

        public readonly List<PaintText> Texts = new List<PaintText>();
        public readonly List<int> Separators = new List<int>();
        public Color LedColor = Theme.TextFaint;
        public Rectangle LedRect;
        /// <summary>Brand logo painted at LogoAt (drawn at its own pixel size).</summary>
        public Image Logo;
        public Point LogoAt;

        public RemotePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Window;
        }

        public void AddText(string text, RectangleF rect, Font font, Color color, StringAlignment align)
        {
            Texts.Add(new PaintText { Text = text, Rect = rect, Font = font, Color = color, Align = align });
        }

        public RectangleF Body { get { return new RectangleF(8, 8, Width - 17, Math.Max(DisplayRectangle.Height, ClientSize.Height) - 17); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            RectangleF body = Body;
            using (GraphicsPath sh = Theme.Rounded(new RectangleF(body.X, body.Y + 4, body.Width, body.Height), 28))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillPath(b, sh);
            using (GraphicsPath p = Theme.Rounded(body, 28))
            {
                using (LinearGradientBrush lb = new LinearGradientBrush(new PointF(0, body.Top), new PointF(0, body.Bottom), Theme.BodyTop, Theme.BodyBottom))
                    g.FillPath(lb, p);
                using (Pen edge = new Pen(Color.FromArgb(70, 70, 76), 1.2f)) g.DrawPath(edge, p);
            }
            using (GraphicsPath hl = Theme.Rounded(new RectangleF(body.X + 1.5f, body.Y + 1.5f, body.Width - 3, body.Height - 3), 27))
            using (Pen pen = new Pen(Color.FromArgb(18, 255, 255, 255), 1f))
                g.DrawPath(pen, hl);

            foreach (int y in Separators)
            {
                using (Pen dark = new Pen(Color.FromArgb(14, 14, 16))) g.DrawLine(dark, body.X + 22, y, body.Right - 22, y);
                using (Pen light = new Pen(Color.FromArgb(60, 60, 66))) g.DrawLine(light, body.X + 22, y + 1, body.Right - 22, y + 1);
            }

            if (Logo != null) g.DrawImageUnscaled(Logo, LogoAt);

            foreach (PaintText t in Texts)
            {
                using (StringFormat sf = new StringFormat { Alignment = t.Align, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                using (SolidBrush b = new SolidBrush(t.Color))
                    g.DrawString(t.Text, t.Font, b, t.Rect, sf);
            }

            if (!LedRect.IsEmpty)
            {
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(60, LedColor)))
                    g.FillEllipse(glow, LedRect.X - 4, LedRect.Y - 4, LedRect.Width + 8, LedRect.Height + 8);
                using (SolidBrush led = new SolidBrush(LedColor)) g.FillEllipse(led, LedRect);
            }
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }
    }

    /// <summary>Row of LCD-like readouts under the preview.</summary>
    public class ReadoutBar : Control
    {
        class Cell { public string Label = "", Value = "--"; public double Fraction = -1; }
        readonly Cell[] cells;
        public string Status = "";
        public Color StatusColor = Theme.TextDim;

        public ReadoutBar(params string[] labels)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Window;
            cells = new Cell[labels.Length];
            for (int i = 0; i < labels.Length; i++) cells[i] = new Cell { Label = labels[i] };
        }

        public void Set(int i, string value, double fraction)
        {
            Cell c = cells[i];
            if (c.Value == value && Math.Abs(c.Fraction - fraction) < 0.001) return;
            c.Value = value; c.Fraction = fraction;
            Invalidate();
        }

        public void SetStatus(string text, Color color)
        {
            if (Status == text && StatusColor == color) return;
            Status = text; StatusColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            int gap = 8;
            int cellH = 54;
            float cw = (Width - gap * (cells.Length - 1)) / (float)cells.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                RectangleF r = new RectangleF(i * (cw + gap), 0, cw, cellH);
                using (GraphicsPath p = Theme.Rounded(r, 10))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 22, 25))) g.FillPath(b, p);
                    using (Pen pen = new Pen(Color.FromArgb(44, 44, 50))) g.DrawPath(pen, p);
                }
                using (SolidBrush lb = new SolidBrush(Theme.TextFaint))
                    g.DrawString(cells[i].Label, Theme.Small, lb, r.X + 12, r.Y + 7);
                using (SolidBrush vb = new SolidBrush(Theme.Text))
                using (StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(cells[i].Value, Theme.Big, vb, new RectangleF(r.X + 10, r.Y + 20, r.Width - 16, 24), sf);
                if (cells[i].Fraction >= 0)
                {
                    RectangleF track = new RectangleF(r.X + 12, r.Bottom - 7, r.Width - 24, 3);
                    using (SolidBrush tb = new SolidBrush(Color.FromArgb(40, 40, 46))) g.FillRectangle(tb, track);
                    using (SolidBrush fb = new SolidBrush(Color.FromArgb(170, 170, 178)))
                        g.FillRectangle(fb, track.X, track.Y, (float)(track.Width * Math.Max(0, Math.Min(1, cells[i].Fraction))), track.Height);
                }
            }
            if (Status.Length > 0)
                using (SolidBrush sb = new SolidBrush(StatusColor))
                using (StringFormat sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(Status, Theme.Normal, sb, new RectangleF(2, cellH + 8, Width - 4, 22), sf);
        }
    }

    /// <summary>
    /// Schematic pan/tilt map for cameras without an in-app video feed (VISCA network/serial cameras).
    /// </summary>
    public class PositionMap : Control
    {
        public PtzCamera Camera;
        public Func<int, Preset> PresetLookup;
        public string Note = "";

        public PositionMap()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Color.Black);
            if (Camera == null) return;
            Rectangle field = new Rectangle(40, 40, Width - 80, Height - 120);
            if (field.Width < 50 || field.Height < 50) return;

            using (SolidBrush bg = new SolidBrush(Color.FromArgb(14, 14, 16))) g.FillRectangle(bg, field);
            using (Pen grid = new Pen(Color.FromArgb(34, 34, 38)))
            {
                for (int i = 1; i < 8; i++) g.DrawLine(grid, field.X + field.Width * i / 8, field.Y, field.X + field.Width * i / 8, field.Bottom);
                for (int i = 1; i < 4; i++) g.DrawLine(grid, field.X, field.Y + field.Height * i / 4, field.Right, field.Y + field.Height * i / 4);
            }
            using (Pen border = new Pen(Color.FromArgb(60, 60, 66))) g.DrawRectangle(border, field);

            AxisRange pr = Camera.PanRange, tr = Camera.TiltRange;
            Func<int, int, PointF> map = delegate(int pan, int tilt)
            {
                return new PointF(field.X + (float)((pan - pr.Min) / pr.Span) * field.Width,
                                  field.Bottom - (float)((tilt - tr.Min) / tr.Span) * field.Height);
            };

            PointF home = map(pr.Home, tr.Home);
            using (Pen hp = new Pen(Color.FromArgb(70, 70, 76)) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(hp, home.X, field.Y, home.X, field.Bottom);
                g.DrawLine(hp, field.X, home.Y, field.Right, home.Y);
            }

            if (PresetLookup != null)
            {
                for (int slot = 1; slot <= 12; slot++)
                {
                    Preset p = PresetLookup(slot);
                    if (p == null) continue;
                    PointF pt = map(p.Pan, p.Tilt);
                    using (Pen pen = new Pen(Theme.TextDim, 1.5f)) g.DrawEllipse(pen, pt.X - 9, pt.Y - 9, 18, 18);
                    Theme.Centered(g, slot.ToString(), Theme.Small, Theme.TextDim, new RectangleF(pt.X - 9, pt.Y - 9, 18, 18));
                }
            }

            PtzState s = Camera.State;
            PointF cur = map(s.Pan, s.Tilt);
            double zf = Camera.ZoomRange.Supported ? (s.Zoom - Camera.ZoomRange.Min) / Camera.ZoomRange.Span : 0;
            float box = (float)(60 - 44 * zf);
            using (Pen fov = new Pen(Color.FromArgb(200, 220, 220, 226), 1.5f))
                g.DrawRectangle(fov, cur.X - box * 0.89f, cur.Y - box / 2, box * 1.78f, box);
            using (SolidBrush dot = new SolidBrush(Color.White)) g.FillEllipse(dot, cur.X - 5, cur.Y - 5, 10, 10);

            using (SolidBrush t = new SolidBrush(Theme.TextFaint))
            {
                g.DrawString("PAN →", Theme.Small, t, field.Right - 50, field.Bottom + 6);
                g.DrawString("TILT ↑", Theme.Small, t, field.X - 34, field.Y - 18);
            }
            using (SolidBrush nb = new SolidBrush(Theme.TextDim))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(Note, Theme.Normal, nb, new RectangleF(20, field.Bottom + 26, Width - 40, 50), sf);
        }
    }
}
