using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PTZVC
{
    /// <summary>
    /// "PTZVC.exe --virtual-camera": a stand-alone simulated PTZ camera that is controlled over the
    /// network with VISCA, exactly like a real IP PTZ camera. Used to test PTZVC without hardware.
    /// </summary>
    public class VirtualCameraForm : Form
    {
        readonly SimModel model = new SimModel();
        readonly ViscaServer server;
        readonly SimView view;
        readonly ListBox log;
        readonly Label status;
        readonly Queue<string> pending = new Queue<string>();
        readonly System.Windows.Forms.Timer timer;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Mutex runningFlag;
        double last;

        public VirtualCameraForm(string[] args)
        {
            bool created;
            runningFlag = new Mutex(false, MainForm.VirtualCameraMutex, out created);

            Text = "PTZVC — Virtual VISCA Camera";
            Theme.DarkTitleBar(this);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Normal;
            ClientSize = new Size(1180, 600);
            MinimumSize = new Size(800, 420);
            StartPosition = FormStartPosition.Manual;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 20, wa.Top + 20);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            model.DepthProbe = SimScene.DepthAt;
            server = new ViscaServer(model);
            server.SonyUdpPort = IntArg(args, "--sony-port", Visca.SonyUdpPort);
            server.TcpPort = IntArg(args, "--tcp-port", Visca.RawTcpPort);
            server.RawUdpPort = IntArg(args, "--udp-port", Visca.RawUdpPort);
            server.Log += delegate(string who, string hex, string meaning)
            {
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + meaning + (hex.Length > 0 ? "   [" + hex + "]" : "");
                lock (pending) { pending.Enqueue(line); while (pending.Count > 500) pending.Dequeue(); }
            };
            server.Start();

            view = new SimView(model) { Dock = DockStyle.Fill, Caption = "VIRTUAL VISCA CAMERA", Footer = "VISCA · address 1" };

            Panel side = new Panel { Dock = DockStyle.Right, Width = 420, BackColor = Theme.Panel, Padding = new Padding(12) };
            Label head = new Label { Text = "VISCA TRAFFIC", Font = Theme.Label, ForeColor = Theme.TextDim, Dock = DockStyle.Top, Height = 22 };
            log = new ListBox
            {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 16, 18), ForeColor = Theme.TextDim, BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8.5f), IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 17, SelectionMode = SelectionMode.None
            };
            log.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                using (SolidBrush bg = new SolidBrush(log.BackColor)) e.Graphics.FillRectangle(bg, e.Bounds);
                string t = (string)log.Items[e.Index];
                Color c = t.Contains("Inquiry") ? Theme.TextFaint : (t.Contains("error") || t.Contains("ignored") ? Theme.Bad : Theme.Text);
                TextRenderer.DrawText(e.Graphics, t, log.Font, e.Bounds, c, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            CheckBox showInq = new CheckBox { Text = "Show position inquiries (polling)", ForeColor = Theme.TextDim, Dock = DockStyle.Bottom, Height = 26, Checked = false, Name = "showInq" };
            side.Controls.Add(log);
            side.Controls.Add(showInq);
            side.Controls.Add(head);

            status = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(14, 8, 14, 4), Font = Theme.Normal, ForeColor = Theme.TextDim, BackColor = Theme.Window };
            string listening = server.Listening.Count > 0 ? string.Join("  ·  ", server.Listening.ToArray()) : "nothing";
            status.Text = "Listening on " + listening + ".   In PTZVC: ⋯ menu → Launch virtual VISCA camera, or add 127.0.0.1 manually.";
            if (server.Failed.Count > 0)
            {
                status.ForeColor = Theme.Bad;
                status.Text += "\nCould not open: " + string.Join("; ", server.Failed.ToArray()) + "  (another virtual camera already running?)";
            }

            Controls.Add(view);
            Controls.Add(side);
            Controls.Add(status);
            view.BringToFront();

            timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += delegate
            {
                double now = clock.Elapsed.TotalSeconds;
                model.Step(now - last);
                last = now;
                view.Invalidate();
                DrainLog(showInq.Checked);
            };
            timer.Start();
        }

        static int IntArg(string[] args, string name, int def)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                int v;
                if (args[i] == name && int.TryParse(args[i + 1], out v)) return v;
            }
            return def;
        }

        void DrainLog(bool showInquiries)
        {
            List<string> lines = new List<string>();
            lock (pending) { while (pending.Count > 0) lines.Add(pending.Dequeue()); }
            if (lines.Count == 0) return;
            log.BeginUpdate();
            foreach (string l in lines)
            {
                if (!showInquiries && l.Contains("Inquiry")) continue;
                log.Items.Add(l);
            }
            while (log.Items.Count > 400) log.Items.RemoveAt(0);
            log.EndUpdate();
            if (log.Items.Count > 0) log.TopIndex = log.Items.Count - 1;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            server.Dispose();
            runningFlag.Dispose();
            base.OnFormClosed(e);
        }
    }
}
