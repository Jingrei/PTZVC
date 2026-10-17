using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace PTZVC
{
    static class DarkUi
    {
        public static Button Button(string text, int width)
        {
            Button b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(52, 52, 58), ForeColor = Theme.Text,
                Font = Theme.Label, Height = 34, Width = width, Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = Theme.Edge;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 78);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 30, 33);
            return b;
        }

        public static TextBox TextBox()
        {
            return new TextBox { BackColor = Color.FromArgb(34, 34, 38), ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Normal };
        }

        public static Label Label(string text)
        {
            return new Label { Text = text, ForeColor = Theme.TextDim, Font = Theme.Label, AutoSize = true, BackColor = Color.Transparent };
        }

        public static void Style(Form f, string title)
        {
            f.Text = title;
            f.BackColor = Theme.Panel;
            f.ForeColor = Theme.Text;
            f.Font = Theme.Normal;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            Theme.DarkTitleBar(f);
            f.StartPosition = FormStartPosition.CenterParent;
            f.AutoScaleDimensions = new SizeF(96f, 96f);
            f.AutoScaleMode = AutoScaleMode.Dpi;
        }
    }

    /// <summary>Dark message box. Lines containing a tab are drawn as two columns.</summary>
    public class DarkMessage : Form
    {
        readonly string[] lines;
        readonly Image logo;
        int Pad = 22, Col2 = 190, LineH = 22, TextTop;

        DarkMessage(string title, string text, string logoName)
        {
            DarkUi.Style(this, title);
            AutoScaleMode = AutoScaleMode.None; // scaled by hand below
            lines = text.Split('\n');
            float k;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
            Pad = (int)(Pad * k); Col2 = (int)(Col2 * k); LineH = (int)(LineH * k);
            if (logoName != null) logo = Brand.Tinted(logoName, Theme.Text, (int)(120 * k));
            TextTop = logo == null ? Pad : Pad * 2 + logo.Height;
            int w = (int)(520 * k);
            int h = TextTop + Pad + lines.Length * LineH + (int)(50 * k);
            ClientSize = new Size(w, h);
            Button ok = DarkUi.Button("OK", (int)(100 * k));
            ok.Height = (int)(34 * k);
            ok.Location = new Point(w - Pad - ok.Width, h - Pad - ok.Height + 6);
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);
            AcceptButton = ok; CancelButton = ok;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (logo != null) e.Graphics.DrawImageUnscaled(logo, (ClientSize.Width - logo.Width) / 2, Pad);
            int y = TextTop;
            foreach (string line in lines)
            {
                int tab = line.IndexOf('\t');
                if (tab >= 0)
                {
                    TextRenderer.DrawText(e.Graphics, line.Substring(0, tab), Theme.Label, new Point(Pad, y), Theme.Text);
                    TextRenderer.DrawText(e.Graphics, line.Substring(tab + 1), Theme.Normal, new Point(Pad + Col2, y), Theme.TextDim);
                }
                else TextRenderer.DrawText(e.Graphics, line, Theme.Normal, new Point(Pad, y), Theme.Text);
                y += LineH;
            }
        }

        public static void Show(IWin32Window owner, string title, string text)
        {
            Show(owner, title, text, null);
        }

        /// <summary>logoName: one of the Brand logos, shown above the text.</summary>
        public static void Show(IWin32Window owner, string title, string text, string logoName)
        {
            using (DarkMessage m = new DarkMessage(title, text, logoName)) m.ShowDialog(owner);
        }
    }

    /// <summary>Add or edit a VISCA (IP / serial) camera.</summary>
    public class AddViscaDialog : Form
    {
        public ViscaEntry Result;
        public Action LaunchVirtual;

        readonly TextBox nameBox, hostBox, portBox;
        readonly DarkCombo protoCombo, comCombo, addrCombo;
        readonly Label hostLabel, portLabel, error;

        static readonly ViscaProtocol[] Protocols = { ViscaProtocol.SonyUdp, ViscaProtocol.RawTcp, ViscaProtocol.RawUdp, ViscaProtocol.Serial };

        public AddViscaDialog(ViscaEntry existing)
        {
            DarkUi.Style(this, existing == null ? "Add VISCA camera" : "Edit VISCA camera");
            ClientSize = new Size(480, 452);
            int x = 24, fx = 170, fw = 286;

            Label head = new Label
            {
                Text = "Professional PTZ cameras (Sony, PTZOptics, Lumens, BirdDog, Marshall, AVer, …) are controlled with VISCA over the network or a serial cable.",
                ForeColor = Theme.TextDim, Font = Theme.Normal, Location = new Point(x, 18), Size = new Size(432, 44)
            };
            Controls.Add(head);

            int y = 76;
            Controls.Add(Lbl("Name", x, y));
            nameBox = DarkUi.TextBox(); nameBox.SetBounds(fx, y - 3, fw, 26); Controls.Add(nameBox);

            y += 44;
            Controls.Add(Lbl("Protocol", x, y));
            protoCombo = new DarkCombo(); protoCombo.SetBounds(fx, y - 4, fw, 28);
            foreach (ViscaProtocol p in Protocols) protoCombo.Items.Add(Visca.ProtocolName(p));
            Controls.Add(protoCombo);

            y += 44;
            hostLabel = Lbl("IP address", x, y); Controls.Add(hostLabel);
            hostBox = DarkUi.TextBox(); hostBox.SetBounds(fx, y - 3, fw, 26); Controls.Add(hostBox);
            comCombo = new DarkCombo(); comCombo.SetBounds(fx, y - 4, fw, 28); Controls.Add(comCombo);
            foreach (string port in SerialPort.GetPortNames()) comCombo.Items.Add(port);
            if (comCombo.Items.Count == 0) comCombo.Items.Add("(no COM ports found)");
            comCombo.SelectedIndex = 0;

            y += 44;
            portLabel = Lbl("Port", x, y); Controls.Add(portLabel);
            portBox = DarkUi.TextBox(); portBox.SetBounds(fx, y - 3, 110, 26); Controls.Add(portBox);

            y += 44;
            Controls.Add(Lbl("Camera address", x, y));
            addrCombo = new DarkCombo(); addrCombo.SetBounds(fx, y - 4, 110, 28);
            for (int i = 1; i <= 7; i++) addrCombo.Items.Add(i.ToString());
            Controls.Add(addrCombo);

            y += 44;
            Label hint = new Label
            {
                Text = "No camera? “Use virtual camera” starts PTZVC’s virtual VISCA camera on this PC and fills in 127.0.0.1 / UDP 52381.",
                ForeColor = Theme.TextFaint, Font = Theme.Normal, Location = new Point(x, y), Size = new Size(432, 40)
            };
            Controls.Add(hint);

            error = new Label { ForeColor = Theme.Bad, Font = Theme.Normal, Location = new Point(x, y + 42), Size = new Size(432, 22) };
            Controls.Add(error);

            Button virt = DarkUi.Button("Use virtual camera", 150);
            Button cancel = DarkUi.Button("Cancel", 90);
            Button ok = DarkUi.Button(existing == null ? "Add && connect" : "Save && connect", 130);
            int by = ClientSize.Height - 24 - 34;
            virt.Location = new Point(x, by);
            cancel.Location = new Point(ClientSize.Width - 24 - 130 - 8 - 90, by);
            ok.Location = new Point(ClientSize.Width - 24 - 130, by);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(virt); Controls.Add(cancel); Controls.Add(ok);
            AcceptButton = ok; CancelButton = cancel;

            protoCombo.SelectedIndexChanged += delegate { ProtocolChanged(true); };
            virt.Click += delegate
            {
                nameBox.Text = "Virtual VISCA Camera";
                protoCombo.SelectedIndex = 0;
                hostBox.Text = "127.0.0.1";
                portBox.Text = Visca.SonyUdpPort.ToString();
                addrCombo.SelectedIndex = 0;
                if (LaunchVirtual != null) LaunchVirtual();
                error.ForeColor = Theme.TextDim;
                error.Text = "Virtual camera starting… click “Add & connect”.";
            };
            ok.Click += delegate { Accept(); };

            if (existing != null)
            {
                nameBox.Text = existing.Label;
                protoCombo.SelectedIndex = Array.IndexOf(Protocols, existing.Protocol);
                ProtocolChanged(false);
                if (existing.Protocol == ViscaProtocol.Serial)
                {
                    int idx = comCombo.Items.IndexOf(existing.Host);
                    if (idx < 0) idx = comCombo.Items.Add(existing.Host);
                    comCombo.SelectedIndex = idx;
                }
                else hostBox.Text = existing.Host;
                portBox.Text = existing.Port.ToString();
                addrCombo.SelectedIndex = Math.Max(0, Math.Min(6, existing.Address - 1));
            }
            else
            {
                nameBox.Text = "PTZ Camera";
                hostBox.Text = "192.168.1.100";
                protoCombo.SelectedIndex = 0;
                addrCombo.SelectedIndex = 0;
                ProtocolChanged(true);
            }
            PerformAutoScale();
        }

        static Label Lbl(string text, int x, int y)
        {
            Label l = DarkUi.Label(text);
            l.Location = new Point(x, y);
            return l;
        }

        ViscaProtocol Protocol { get { return Protocols[Math.Max(0, protoCombo.SelectedIndex)]; } }

        void ProtocolChanged(bool resetPort)
        {
            bool serial = Protocol == ViscaProtocol.Serial;
            hostBox.Visible = !serial;
            comCombo.Visible = serial;
            hostLabel.Text = serial ? "COM port" : "IP address";
            portLabel.Text = serial ? "Baud rate" : "Port";
            if (resetPort) portBox.Text = Visca.DefaultPort(Protocol).ToString();
        }

        void Accept()
        {
            error.ForeColor = Theme.Bad;
            string name = nameBox.Text.Trim().Replace("\t", " ");
            if (name.Length == 0) { error.Text = "Please enter a name."; return; }
            int port;
            if (!int.TryParse(portBox.Text.Trim(), out port) || port <= 0 || port > 921600)
            {
                error.Text = Protocol == ViscaProtocol.Serial ? "Enter a baud rate (usually 9600)." : "Enter a port number (1–65535).";
                return;
            }
            string host;
            if (Protocol == ViscaProtocol.Serial)
            {
                host = comCombo.SelectedItem as string;
                if (host == null || host.StartsWith("(")) { error.Text = "No serial port selected."; return; }
            }
            else
            {
                host = hostBox.Text.Trim();
                if (host.Length == 0) { error.Text = "Enter the camera's IP address."; return; }
                if (port > 65535) { error.Text = "Port must be 1–65535."; return; }
            }
            Result = new ViscaEntry { Label = name, Protocol = Protocol, Host = host, Port = port, Address = addrCombo.SelectedIndex + 1 };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
