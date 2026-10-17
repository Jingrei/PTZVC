using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PTZVC
{
    /// <summary>
    /// Emulates a VISCA PTZ camera on top of a SimModel. Speaks Sony VISCA-over-IP (UDP),
    /// raw VISCA over TCP and raw VISCA over UDP, like PTZOptics/Sony/Lumens cameras do.
    /// </summary>
    public class ViscaServer : IDisposable
    {
        public readonly SimModel Model;
        public int SonyUdpPort = Visca.SonyUdpPort, TcpPort = Visca.RawTcpPort, RawUdpPort = Visca.RawUdpPort;

        /// <summary>Raised on network threads: (endpoint, request hex, meaning).</summary>
        public event Action<string, string, string> Log;
        public readonly List<string> Listening = new List<string>();
        public readonly List<string> Failed = new List<string>();

        readonly Dictionary<int, PtzState> cameraPresets = new Dictionary<int, PtzState>();
        readonly List<IDisposable> sockets = new List<IDisposable>();
        volatile bool running;

        public ViscaServer(SimModel model) { Model = model; }

        public void Start()
        {
            running = true;
            try
            {
                UdpClient u = new UdpClient(new IPEndPoint(IPAddress.Any, SonyUdpPort));
                sockets.Add(u);
                StartThread(delegate { UdpLoop(u, true); }, "Sony UDP");
                Listening.Add("UDP " + SonyUdpPort + " (Sony VISCA over IP)");
            }
            catch (Exception ex) { Failed.Add("UDP " + SonyUdpPort + ": " + ex.Message); }
            try
            {
                TcpListener l = new TcpListener(IPAddress.Any, TcpPort);
                l.Start();
                sockets.Add(new ListenerHandle(l));
                StartThread(delegate { AcceptLoop(l); }, "TCP accept");
                Listening.Add("TCP " + TcpPort + " (raw VISCA)");
            }
            catch (Exception ex) { Failed.Add("TCP " + TcpPort + ": " + ex.Message); }
            try
            {
                UdpClient u = new UdpClient(new IPEndPoint(IPAddress.Any, RawUdpPort));
                sockets.Add(u);
                StartThread(delegate { UdpLoop(u, false); }, "raw UDP");
                Listening.Add("UDP " + RawUdpPort + " (raw VISCA)");
            }
            catch (Exception ex) { Failed.Add("UDP " + RawUdpPort + ": " + ex.Message); }
        }

        class ListenerHandle : IDisposable
        {
            readonly TcpListener l;
            public ListenerHandle(TcpListener l) { this.l = l; }
            public void Dispose() { l.Stop(); }
        }

        static void StartThread(ThreadStart fn, string name)
        {
            Thread t = new Thread(fn) { IsBackground = true, Name = "VISCA server " + name };
            t.Start();
        }

        public void Dispose()
        {
            running = false;
            foreach (IDisposable s in sockets) { try { s.Dispose(); } catch { } }
            sockets.Clear();
        }

        // ---- network loops ---------------------------------------------------------

        void UdpLoop(UdpClient u, bool sony)
        {
            while (running)
            {
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data;
                try { data = u.Receive(ref from); }
                catch (SocketException) { if (!running) return; continue; } // ICMP reset from a closed client
                catch (ObjectDisposedException) { return; }
                try
                {
                    if (sony) HandleSony(u, from, data);
                    else
                    {
                        Visca.Splitter sp = new Visca.Splitter();
                        foreach (byte[] m in sp.Feed(data, data.Length))
                            foreach (byte[] r in Handle(m, "udp " + from))
                                u.Send(r, r.Length, from);
                    }
                }
                catch (Exception) { }
            }
        }

        void HandleSony(UdpClient u, IPEndPoint from, byte[] data)
        {
            if (data.Length < 8) return;
            int type = (data[0] << 8) | data[1];
            int len = (data[2] << 8) | data[3];
            if (data.Length < 8 + len) return;
            byte[] payload = new byte[len];
            Buffer.BlockCopy(data, 8, payload, 0, len);
            if (type == 0x0200)
            {
                // control command (sequence reset) -> control reply
                byte[] reply = { 0x02, 0x01, 0x00, 0x01, data[4], data[5], data[6], data[7], 0x01 };
                u.Send(reply, reply.Length, from);
                Emit("sony " + from, Visca.Hex(payload), "RESET sequence number");
                return;
            }
            if (type != 0x0100 && type != 0x0110) return;
            foreach (byte[] r in Handle(payload, "sony " + from))
            {
                byte[] pkt = new byte[8 + r.Length];
                pkt[0] = 0x01; pkt[1] = 0x11;
                pkt[2] = (byte)(r.Length >> 8); pkt[3] = (byte)r.Length;
                pkt[4] = data[4]; pkt[5] = data[5]; pkt[6] = data[6]; pkt[7] = data[7];
                Buffer.BlockCopy(r, 0, pkt, 8, r.Length);
                u.Send(pkt, pkt.Length, from);
            }
        }

        void AcceptLoop(TcpListener l)
        {
            while (running)
            {
                TcpClient c;
                try { c = l.AcceptTcpClient(); }
                catch { return; }
                c.NoDelay = true;
                StartThread(delegate { TcpClientLoop(c); }, "TCP client");
            }
        }

        void TcpClientLoop(TcpClient c)
        {
            string who = "tcp " + c.Client.RemoteEndPoint;
            Emit(who, "", "client connected");
            using (c)
            {
                NetworkStream s = c.GetStream();
                byte[] buf = new byte[256];
                Visca.Splitter sp = new Visca.Splitter();
                while (running)
                {
                    int n;
                    try { n = s.Read(buf, 0, buf.Length); } catch { break; }
                    if (n <= 0) break;
                    foreach (byte[] m in sp.Feed(buf, n))
                        foreach (byte[] r in Handle(m, who))
                        {
                            try { s.Write(r, 0, r.Length); } catch { return; }
                        }
                }
            }
            Emit(who, "", "client disconnected");
        }

        void Emit(string who, string hex, string meaning)
        {
            Action<string, string, string> h = Log;
            if (h != null) h(who, hex, meaning);
        }

        // ---- VISCA decoding -----------------------------------------------------------

        static double PanSpeedDeg(int vv) { return 2 * Math.Pow(40, (Math.Max(1, Math.Min(0x18, vv)) - 1) / 23.0); }
        static double TiltSpeedDeg(int ww) { return 1.6 * Math.Pow(40, (Math.Max(1, Math.Min(0x14, ww)) - 1) / 19.0); }
        static double DriveRate(int p) { return 0.06 * Math.Pow(8, Math.Max(0, Math.Min(7, p)) / 7.0); }

        /// <summary>Processes one VISCA message and returns the reply messages.</summary>
        public List<byte[]> Handle(byte[] m, string who)
        {
            List<byte[]> outList = new List<byte[]>();
            string hex = Visca.Hex(m);
            if (m.Length < 3 || m[m.Length - 1] != 0xFF || (m[0] & 0xF0) != 0x80)
            {
                Emit(who, hex, "ignored (not a VISCA message)");
                return outList;
            }
            int addr = m[0] & 0x0F;
            if (addr != 8 && addr != 1)
            {
                Emit(who, hex, "ignored (addressed to camera " + addr + ")");
                return outList;
            }
            byte y = 0x90;

            // broadcast: address set / IF_Clear
            if (m[0] == 0x88 && m.Length == 4 && m[1] == 0x30)
            {
                outList.Add(new byte[] { 0x88, 0x30, (byte)(m[2] + 1), 0xFF });
                Emit(who, hex, "AddressSet");
                return outList;
            }
            if (m.Length == 5 && m[1] == 0x01 && m[2] == 0x00 && m[3] == 0x01)
            {
                outList.Add(new byte[] { y, 0x50, 0xFF });
                Emit(who, hex, "IF_Clear");
                return outList;
            }

            string meaning = null;
            if (m[1] == 0x01) meaning = Command(m);
            else if (m[1] == 0x09)
            {
                byte[] reply = Inquiry(m, out meaning);
                if (reply != null)
                {
                    outList.Add(reply);
                    Emit(who, hex, meaning);
                    return outList;
                }
            }

            if (meaning == null)
            {
                outList.Add(new byte[] { y, 0x60, 0x02, 0xFF }); // syntax error
                Emit(who, hex, "unsupported -> syntax error");
                return outList;
            }
            outList.Add(new byte[] { y, 0x41, 0xFF });  // ACK (socket 1)
            outList.Add(new byte[] { y, 0x51, 0xFF });  // completion
            Emit(who, hex, meaning);
            return outList;
        }

        /// <summary>m is a full message: 8x 01 ... FF, so n counts the header and terminator.</summary>
        string Command(byte[] m)
        {
            int n = m.Length;
            if (n == 9 && m[2] == 0x06 && m[3] == 0x01)
            {
                int pd = m[6] == 0x01 ? -1 : m[6] == 0x02 ? 1 : 0;
                int td = m[7] == 0x01 ? 1 : m[7] == 0x02 ? -1 : 0;
                Model.DrivePanTilt(pd, td, PanSpeedDeg(m[4]), TiltSpeedDeg(m[5]));
                if (pd == 0 && td == 0) return "Pan-tilt STOP";
                return "Pan-tilt drive " + (td > 0 ? "up " : td < 0 ? "down " : "") + (pd < 0 ? "left " : pd > 0 ? "right " : "") +
                    "(speed " + m[4] + "/" + m[5] + ")";
            }
            if (n == 15 && m[2] == 0x06 && (m[3] == 0x02 || m[3] == 0x03))
            {
                int p = Visca.ReadNibbles(m, 6, true), t = Visca.ReadNibbles(m, 10, true);
                if (m[3] == 0x03)
                {
                    PtzState s = Model.Snapshot();
                    p += s.Pan; t += s.Tilt;
                }
                Model.SetPanTilt(p, t, Math.Max(20, PanSpeedDeg(m[4])), Math.Max(16, TiltSpeedDeg(m[5])));
                return (m[3] == 0x02 ? "Pan-tilt absolute " : "Pan-tilt relative -> ") + "pan " + p + " tilt " + t;
            }
            if (n == 5 && m[2] == 0x06 && (m[3] == 0x04 || m[3] == 0x05))
            {
                Model.Home();
                return m[3] == 0x04 ? "Pan-tilt HOME" : "Pan-tilt RESET";
            }
            if (n == 6 && m[2] == 0x04 && m[3] == 0x07)
            {
                int v = m[4];
                if (v == 0x00) { Model.DriveZoom(0, 0); return "Zoom STOP"; }
                if (v == 0x02) { Model.DriveZoom(1, DriveRate(3)); return "Zoom tele"; }
                if (v == 0x03) { Model.DriveZoom(-1, DriveRate(3)); return "Zoom wide"; }
                if ((v & 0xF0) == 0x20) { Model.DriveZoom(1, DriveRate(v & 0x0F)); return "Zoom tele (speed " + (v & 0x0F) + ")"; }
                if ((v & 0xF0) == 0x30) { Model.DriveZoom(-1, DriveRate(v & 0x0F)); return "Zoom wide (speed " + (v & 0x0F) + ")"; }
                return null;
            }
            if (n == 6 && m[2] == 0x04 && m[3] == 0x08)
            {
                int v = m[4];
                if (Model.AutoFocus && v != 0) return "Focus drive ignored (auto focus on)";
                if (v == 0x00) { Model.DriveFocus(0, 0); return "Focus STOP"; }
                if (v == 0x02) { Model.DriveFocus(-1, DriveRate(3)); return "Focus far"; }
                if (v == 0x03) { Model.DriveFocus(1, DriveRate(3)); return "Focus near"; }
                if ((v & 0xF0) == 0x20) { Model.DriveFocus(-1, DriveRate(v & 0x0F)); return "Focus far (speed " + (v & 0x0F) + ")"; }
                if ((v & 0xF0) == 0x30) { Model.DriveFocus(1, DriveRate(v & 0x0F)); return "Focus near (speed " + (v & 0x0F) + ")"; }
                return null;
            }
            if (n == 9 && m[2] == 0x04 && m[3] == 0x47)
            {
                int z = Visca.ReadNibbles(m, 4, false);
                Model.SetZoom(z);
                return "Zoom direct " + z;
            }
            if (n == 13 && m[2] == 0x04 && m[3] == 0x47)
            {
                int z = Visca.ReadNibbles(m, 4, false), f = Visca.ReadNibbles(m, 8, false);
                Model.SetZoom(z);
                if (!Model.AutoFocus) Model.SetFocus(f);
                return "Zoom+focus direct " + z + " / " + f;
            }
            if (n == 9 && m[2] == 0x04 && m[3] == 0x48)
            {
                int f = Visca.ReadNibbles(m, 4, false);
                if (Model.AutoFocus) return "Focus direct ignored (auto focus on)";
                Model.SetFocus(f);
                return "Focus direct " + f;
            }
            if (n == 6 && m[2] == 0x04 && m[3] == 0x38)
            {
                if (m[4] == 0x02) { Model.SetAutoFocus(true); return "Focus mode AUTO"; }
                if (m[4] == 0x03) { Model.SetAutoFocus(false); return "Focus mode MANUAL"; }
                if (m[4] == 0x10) { Model.SetAutoFocus(!Model.AutoFocus); return "Focus mode toggle"; }
                return null;
            }
            if (n == 6 && m[2] == 0x04 && m[3] == 0x18) return "One-push AF";
            if (n == 6 && m[2] == 0x04 && m[3] == 0x00) return "Power " + (m[4] == 0x02 ? "on" : "off");
            if (n == 7 && m[2] == 0x04 && m[3] == 0x3F)
            {
                int slot = m[5];
                if (m[4] == 0x01) { lock (cameraPresets) cameraPresets[slot] = Model.Snapshot(); return "Camera memory SET " + slot; }
                if (m[4] == 0x02)
                {
                    PtzState s;
                    bool ok;
                    lock (cameraPresets) ok = cameraPresets.TryGetValue(slot, out s);
                    if (ok)
                    {
                        Model.SetPanTilt(s.Pan, s.Tilt, 80, 64);
                        Model.SetZoom(s.Zoom);
                    }
                    return "Camera memory RECALL " + slot;
                }
                if (m[4] == 0x00) { lock (cameraPresets) cameraPresets.Remove(slot); return "Camera memory RESET " + slot; }
                return null;
            }
            return null;
        }

        byte[] Inquiry(byte[] m, out string meaning)
        {
            meaning = null;
            PtzState s = Model.Snapshot();
            List<byte> r = new List<byte> { 0x90, 0x50 };
            if (m.Length == 5 && m[2] == 0x06 && m[3] == 0x12)
            {
                Visca.PutNibbles(r, s.Pan); Visca.PutNibbles(r, s.Tilt);
                meaning = "Inquiry pan/tilt -> " + s.Pan + " / " + s.Tilt;
            }
            else if (m.Length == 5 && m[2] == 0x04 && m[3] == 0x47) { Visca.PutNibbles(r, s.Zoom); meaning = "Inquiry zoom -> " + s.Zoom; }
            else if (m.Length == 5 && m[2] == 0x04 && m[3] == 0x48) { Visca.PutNibbles(r, s.Focus); meaning = "Inquiry focus -> " + s.Focus; }
            else if (m.Length == 5 && m[2] == 0x04 && m[3] == 0x38) { r.Add((byte)(s.AutoFocus ? 0x02 : 0x03)); meaning = "Inquiry focus mode -> " + (s.AutoFocus ? "auto" : "manual"); }
            else if (m.Length == 5 && m[2] == 0x04 && m[3] == 0x00) { r.Add(0x02); meaning = "Inquiry power -> on"; }
            else if (m.Length == 5 && m[2] == 0x00 && m[3] == 0x02)
            {
                r.AddRange(new byte[] { 0x00, 0x20, 0x05, 0x11, 0x01, 0x00, 0x02 });
                meaning = "Inquiry version";
            }
            else return null;
            r.Add(0xFF);
            return r.ToArray();
        }
    }
}
