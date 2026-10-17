using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PTZVC
{
    public partial class MainForm : Form
    {
        // layout scale for high-DPI screens
        readonly float dpi = 1f;
        int S(double v) { return (int)Math.Round(v * dpi); }

        RemotePanel remote;
        DPad dpad;
        DarkSlider speedSlider, zoomSlider, focusSlider;
        RemoteButton zoomWide, zoomTele, focusFar, focusNear, saveButton, clearButton, snapButton, refreshButton, menuButton;
        readonly RemoteButton[] presetButtons = new RemoteButton[13]; // 1..12
        ToggleSwitch afToggle;
        RemotePanel.PaintText speedText, afText, presetHint;
        DarkCombo cameraCombo;
        Panel topBar, previewFrame, previewHost;
        ReadoutBar readouts;
        DarkMenu mainMenu;
        ToolTip tips;

        public MainForm()
        {
            using (Graphics g = CreateGraphics()) dpi = g.DpiX / 96f;
            Text = "PTZVC — PTZ Virtual Controller";
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(1280), S(780));
            MinimumSize = new Size(S(1000), S(640));
            KeyPreview = true;
            DoubleBuffered = true;
            Theme.DarkTitleBar(this);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            tips = new ToolTip { InitialDelay = 500 };

            BuildRemote();
            BuildLeft();
            Controls.Add(remote);
            previewFrame.BringToFront();

            Init();
        }

        // ---- left side: title bar, preview, readouts --------------------------------

        void BuildLeft()
        {
            topBar = new DoubleBufferedPanel { Dock = DockStyle.Top, Height = S(60), BackColor = Theme.Window };
            Bitmap logo = Brand.Tinted(Brand.Horizontal, Theme.Text, S(26));
            topBar.Paint += delegate(object s, PaintEventArgs e)
            {
                Theme.Smooth(e.Graphics);
                float textX;
                if (logo != null)
                {
                    e.Graphics.DrawImageUnscaled(logo, S(14), (topBar.Height - logo.Height) / 2);
                    textX = S(14) + logo.Width + S(10);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(Theme.Text))
                        e.Graphics.DrawString("PTZVC", Theme.Title, b, S(14), S(14));
                    textX = S(18) + e.Graphics.MeasureString("PTZVC", Theme.Title).Width;
                }
                Control label = topBar.Controls["camLabel"];
                const string subtitle = "PTZ Virtual Controller";
                if (label == null || textX + e.Graphics.MeasureString(subtitle, Theme.Normal).Width < label.Left - S(8))
                    using (SolidBrush b = new SolidBrush(Theme.TextDim))
                        e.Graphics.DrawString(subtitle, Theme.Normal, b, textX, S(21));
            };

            cameraCombo = new DarkCombo { Width = S(380) };
            snapButton = SmallButton("", "Snapshot: save a picture of the current camera (P).\nSaved as PNG in Pictures\\PTZVC.");
            snapButton.Glyph = Glyph.Camera;
            refreshButton = SmallButton("⟳", "Rescan cameras");
            menuButton = SmallButton("⋯", "More options");
            topBar.Controls.Add(cameraCombo);
            topBar.Controls.Add(snapButton);
            topBar.Controls.Add(refreshButton);
            topBar.Controls.Add(menuButton);
            topBar.Resize += delegate { LayoutTopBar(); };
            Label camLabel = new Label { Text = "CAMERA", Font = Theme.Small, ForeColor = Theme.TextFaint, AutoSize = true, BackColor = Theme.Window, Name = "camLabel" };
            topBar.Controls.Add(camLabel);

            readouts = new ReadoutBar("PAN", "TILT", "ZOOM", "FOCUS", "FOCUS MODE") { Dock = DockStyle.Bottom, Height = S(92) };

            previewHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            previewFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Padding = new Padding(S(14), S(4), S(14), S(12)) };
            Panel border = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 44), Padding = new Padding(1) };
            border.Controls.Add(previewHost);
            previewFrame.Controls.Add(border);

            Panel bottomWrap = new Panel { Dock = DockStyle.Bottom, Height = S(100), Padding = new Padding(S(14), 0, S(14), S(8)), BackColor = Theme.Window };
            readouts.Dock = DockStyle.Fill;
            bottomWrap.Controls.Add(readouts);

            Controls.Add(previewFrame);
            Controls.Add(bottomWrap);
            Controls.Add(topBar);
        }

        RemoteButton SmallButton(string text, string tip)
        {
            RemoteButton b = new RemoteButton { Text = text, Size = new Size(S(40), S(34)), LabelFont = Theme.Big, Radius = 8 };
            tips.SetToolTip(b, tip);
            return b;
        }

        void LayoutTopBar()
        {
            int y = S(14);
            menuButton.Location = new Point(topBar.Width - S(14) - menuButton.Width, y);
            refreshButton.Location = new Point(menuButton.Left - S(6) - refreshButton.Width, y);
            snapButton.Location = new Point(refreshButton.Left - S(6) - snapButton.Width, y);
            int comboW = Math.Max(S(200), Math.Min(S(420), topBar.Width - S(420)));
            cameraCombo.Width = comboW;
            cameraCombo.Location = new Point(snapButton.Left - S(8) - comboW, y + (refreshButton.Height - cameraCombo.Height) / 2);
            Control camLabel = topBar.Controls["camLabel"];
            camLabel.Location = new Point(cameraCombo.Left - camLabel.Width - S(8), y + (refreshButton.Height - camLabel.Height) / 2);
            topBar.Invalidate();
        }

        // ---- right side: the remote ------------------------------------------------

        void BuildRemote()
        {
            remote = new RemotePanel { Dock = DockStyle.Right, Width = S(352), AutoScroll = true };
            remote.AutoScrollMinSize = new Size(0, S(772));
            float L = S(30), W = S(292), R = L + W;

            remote.Logo = Brand.Tinted(Brand.Horizontal, Theme.Text, S(24));
            remote.LogoAt = new Point((int)L, S(22));
            if (remote.Logo == null)
                remote.AddText("PTZVC", new RectangleF(L, S(20), S(160), S(30)), Theme.Title, Theme.Text, StringAlignment.Near);
            remote.AddText("PTZ VIRTUAL CONTROLLER", new RectangleF(L + S(1), S(48), S(220), S(16)), Theme.Small, Theme.TextFaint, StringAlignment.Near);
            remote.LedRect = new Rectangle((int)R - S(14), S(30), S(12), S(12));

            dpad = new DPad { Size = new Size(S(196), S(196)), Location = new Point((int)(L + (W - S(196)) / 2), S(70)) };
            tips.SetToolTip(dpad, "Hold an arrow to pan/tilt (keyboard: arrow keys). CENTER returns home (H).");
            remote.Controls.Add(dpad);

            remote.AddText("SPEED", new RectangleF(L, S(272), S(60), S(24)), Theme.Label, Theme.TextDim, StringAlignment.Near);
            speedSlider = new DarkSlider { Location = new Point((int)L + S(58), S(272)), Size = new Size(S(190), S(24)), Minimum = 0, Maximum = 100 };
            remote.Controls.Add(speedSlider);
            speedText = new RemotePanel.PaintText { Text = "50%", Rect = new RectangleF(R - S(44), S(272), S(44), S(24)), Font = Theme.Label, Color = Theme.Text, Align = StringAlignment.Far };
            remote.Texts.Add(speedText);

            // zoom
            remote.AddText("ZOOM", new RectangleF(L, S(306), S(70), S(42)), Theme.Label, Theme.TextDim, StringAlignment.Near);
            zoomWide = new RemoteButton { Glyph = Glyph.Minus, Text = "WIDE", Location = new Point((int)L + S(76), S(306)), Size = new Size(S(104), S(42)) };
            zoomTele = new RemoteButton { Glyph = Glyph.Plus, Text = "TELE", Location = new Point((int)L + S(188), S(306)), Size = new Size(S(104), S(42)) };
            tips.SetToolTip(zoomWide, "Hold to zoom out (Page Down)");
            tips.SetToolTip(zoomTele, "Hold to zoom in (Page Up)");
            zoomSlider = new DarkSlider { Location = new Point((int)L - S(6), S(352)), Size = new Size((int)W + S(12), S(24)) };
            remote.Controls.Add(zoomWide); remote.Controls.Add(zoomTele); remote.Controls.Add(zoomSlider);

            // focus
            remote.AddText("FOCUS", new RectangleF(L, S(384), S(70), S(42)), Theme.Label, Theme.TextDim, StringAlignment.Near);
            focusFar = new RemoteButton { Glyph = Glyph.Far, Text = "FAR", Location = new Point((int)L + S(76), S(384)), Size = new Size(S(104), S(42)) };
            focusNear = new RemoteButton { Glyph = Glyph.Near, Text = "NEAR", Location = new Point((int)L + S(188), S(384)), Size = new Size(S(104), S(42)) };
            tips.SetToolTip(focusFar, "Hold to focus farther ( [ key ). Turn Auto Focus off first.");
            tips.SetToolTip(focusNear, "Hold to focus nearer ( ] key ). Turn Auto Focus off first.");
            focusSlider = new DarkSlider { Location = new Point((int)L - S(6), S(430)), Size = new Size((int)W + S(12), S(24)) };
            remote.Controls.Add(focusFar); remote.Controls.Add(focusNear); remote.Controls.Add(focusSlider);

            // auto focus
            remote.AddText("AUTO FOCUS", new RectangleF(L, S(462), S(150), S(30)), Theme.Label, Theme.TextDim, StringAlignment.Near);
            afText = new RemotePanel.PaintText { Text = "ON", Rect = new RectangleF(R - S(110), S(462), S(46), S(30)), Font = Theme.Label, Color = Theme.Text, Align = StringAlignment.Far };
            remote.Texts.Add(afText);
            afToggle = new ToggleSwitch { Location = new Point((int)R - S(56), S(463)), Size = new Size(S(56), S(28)), Checked = true };
            tips.SetToolTip(afToggle, "Auto Focus (F). When off, presets also restore the saved focus.");
            remote.Controls.Add(afToggle);

            remote.Separators.Add(S(502));

            // presets
            remote.AddText("PRESETS", new RectangleF(L, S(510), S(100), S(18)), Theme.Small, Theme.TextFaint, StringAlignment.Near);
            presetHint = new RemotePanel.PaintText { Text = "", Rect = new RectangleF(L + S(80), S(510), W - S(80), S(18)), Font = Theme.Small, Color = Theme.TextFaint, Align = StringAlignment.Far };
            remote.Texts.Add(presetHint);
            saveButton = new RemoteButton { Text = "SAVE PRESET", LabelFont = Theme.Label, Location = new Point((int)L, S(532)), Size = new Size(S(196), S(38)), LitColor = Theme.Armed };
            clearButton = new RemoteButton { Text = "CLEAR", LabelFont = Theme.Label, Location = new Point((int)L + S(204), S(532)), Size = new Size(S(88), S(38)), LitColor = Theme.Bad };
            tips.SetToolTip(saveButton, "Click, then click a number to store pan, tilt, zoom and focus there (S).");
            tips.SetToolTip(clearButton, "Click, then click a number to erase that preset.");
            remote.Controls.Add(saveButton); remote.Controls.Add(clearButton);

            int bw = S(92), bh = S(36), gap = S(8);
            for (int i = 1; i <= 12; i++)
            {
                int col = (i - 1) % 3, row = (i - 1) / 3;
                RemoteButton b = new RemoteButton
                {
                    Text = i.ToString(),
                    Location = new Point((int)L + col * (bw + gap), S(580) + row * (bh + S(7))),
                    Size = new Size(bw, bh),
                    Tag = i
                };
                presetButtons[i] = b;
                remote.Controls.Add(b);
            }
            mainMenu = new DarkMenu();
        }

        class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        }
    }
}
