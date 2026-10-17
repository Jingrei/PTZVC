using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PTZVC
{
    public enum ViscaProtocol { SonyUdp, RawTcp, RawUdp, Serial }

    public static class Visca
    {
        public const int SonyUdpPort = 52381;
        public const int RawTcpPort = 5678;
        public const int RawUdpPort = 1259;

        /// <summary>Encodes a 16-bit value as four VISCA nibbles (0p 0q 0r 0s).</summary>
        public static void PutNibbles(List<byte> buf, int value)
        {
            ushort u = unchecked((ushort)value);
            buf.Add((byte)((u >> 12) & 0xF));
            buf.Add((byte)((u >> 8) & 0xF));
            buf.Add((byte)((u >> 4) & 0xF));
            buf.Add((byte)(u & 0xF));
        }

        public static int ReadNibbles(byte[] b, int offset, bool signed)
        {
            int u = ((b[offset] & 0xF) << 12) | ((b[offset + 1] & 0xF) << 8) | ((b[offset + 2] & 0xF) << 4) | (b[offset + 3] & 0xF);
            return signed ? (short)u : u;
        }

        public static string Hex(byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", " ");
        }

        public static string ProtocolName(ViscaProtocol p)
        {
            switch (p)
            {
                case ViscaProtocol.SonyUdp: return "VISCA over IP (UDP, Sony)";
                case ViscaProtocol.RawTcp: return "VISCA TCP (raw)";
                case ViscaProtocol.RawUdp: return "VISCA UDP (raw)";
                default: return "VISCA serial (RS-232/RS-485)";
            }
        }

        public static int DefaultPort(ViscaProtocol p)
        {
            switch (p)
            {
                case ViscaProtocol.SonyUdp: return SonyUdpPort;
                case ViscaProtocol.RawTcp: return RawTcpPort;
                case ViscaProtocol.RawUdp: return RawUdpPort;
                default: return 9600;
            }
        }

        /// <summary>Splits a byte stream into VISCA messages terminated by 0xFF.</summary>
        public class Splitter
        {
            readonly List<byte> pending = new List<byte>();
            public IEnumerable<byte[]> Feed(byte[] data, int count)
            {
                List<byte[]> outList = new List<byte[]>();
                for (int i = 0; i < count; i++)
                {
                    pending.Add(data[i]);
                    if (data[i] == 0xFF)
                    {
                        outList.Add(pending.ToArray());
                        pending.Clear();
                    }
                    else if (pending.Count > 64) pending.Clear(); // garbage guard
                }
                return outList;
            }
        }
    }

    // ---- transports ----------------------------------------------------------

    public abstract class ViscaTransport : IDisposable
    {
        /// <summary>Raised on a background thread with one complete VISCA message.</summary>
        public event Action<byte[]> MessageReceived;
        public abstract void Open();
        public abstract void Close();
        public abstract void Send(byte[] msg, bool inquiry);
        public abstract string Describe { get; }
        public virtual bool IsSerial { get { return false; } }

        protected void Raise(byte[] msg)
        {
            Action<byte[]> h = MessageReceived;
            if (h != null) h(msg);
        }

        public void Dispose() { try { Close(); } catch { } }
    }

    /// <summary>UDP datagrams; optionally wrapped in Sony's 8-byte VISCA-over-IP header.</summary>
    public class UdpTransport : ViscaTransport
    {
        readonly string host; readonly int port; readonly bool sonyHeader;
        UdpClient udp;
        Thread reader;
        volatile bool running;
        uint seq;

        public UdpTransport(string host, int port, bool sonyHeader)
        {
            this.host = host; this.port = port; this.sonyHeader = sonyHeader;
        }

        public override string Describe { get { return (sonyHeader ? "VISCA-IP UDP " : "VISCA UDP ") + host + ":" + port; } }

        public override void Open()
        {
            udp = new UdpClient();
            udp.Connect(host, port);
            running = true;
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "VISCA UDP reader" };
            reader.Start();
            if (sonyHeader)
            {
                // RESET sequence number (control command 0x0200, payload 0x01)
                seq = 0;
                byte[] reset = { 0x02, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0x01 };
                udp.Send(reset, reset.Length);
                seq = 1;
            }
        }

        public override void Close()
        {
            running = false;
            if (udp != null) { udp.Close(); udp = null; }
        }

        public override void Send(byte[] msg, bool inquiry)
        {
            UdpClient u = udp;
            if (u == null) throw new InvalidOperationException("Not connected");
            if (!sonyHeader) { u.Send(msg, msg.Length); return; }
            byte[] pkt = new byte[8 + msg.Length];
            pkt[0] = 0x01; pkt[1] = (byte)(inquiry ? 0x10 : 0x00);
            pkt[2] = (byte)(msg.Length >> 8); pkt[3] = (byte)msg.Length;
            uint s;
            lock (this) s = seq++;
            pkt[4] = (byte)(s >> 24); pkt[5] = (byte)(s >> 16); pkt[6] = (byte)(s >> 8); pkt[7] = (byte)s;
            Buffer.BlockCopy(msg, 0, pkt, 8, msg.Length);
            u.Send(pkt, pkt.Length);
        }

        void ReadLoop()
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                byte[] data;
                try { data = udp.Receive(ref any); }
                catch (SocketException) { if (!running) return; Thread.Sleep(50); continue; } // e.g. ICMP port unreachable
                catch (ObjectDisposedException) { return; }
                catch (NullReferenceException) { return; }
                if (sonyHeader)
                {
                    if (data.Length < 8) continue;
                    int type = (data[0] << 8) | data[1];
                    int len = (data[2] << 8) | data[3];
                    if (type != 0x0111 || data.Length < 8 + len) continue; // only VISCA replies
                    byte[] payload = new byte[len];
                    Buffer.BlockCopy(data, 8, payload, 0, len);
                    Raise(payload);
                }
                else
                {
                    Visca.Splitter sp = new Visca.Splitter();
                    foreach (byte[] m in sp.Feed(data, data.Length)) Raise(m);
                }
            }
        }
    }

    public class TcpTransport : ViscaTransport
    {
        readonly string host; readonly int port;
        TcpClient tcp;
        NetworkStream stream;
        Thread reader;
        volatile bool running;

        public TcpTransport(string host, int port) { this.host = host; this.port = port; }

        public override string Describe { get { return "VISCA TCP " + host + ":" + port; } }

        public override void Open()
        {
            tcp = new TcpClient();
            tcp.NoDelay = true;
            IAsyncResult ar = tcp.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(3000))
            {
                tcp.Close();
                throw new TimeoutException("Could not reach " + host + ":" + port + " (timed out).");
            }
            tcp.EndConnect(ar);
            stream = tcp.GetStream();
            running = true;
            reader = new Thread(ReadLoop) { IsBackground = true, Name = "VISCA TCP reader" };
            reader.Start();
        }

        public override void Close()
        {
            running = false;
            if (tcp != null) { tcp.Close(); tcp = null; }
        }

        public override void Send(byte[] msg, bool inquiry)
        {
            NetworkStream s = stream;
            if (s == null || tcp == null) throw new InvalidOperationException("Not connected");
            lock (this) s.Write(msg, 0, msg.Length);
        }

        void ReadLoop()
        {
            byte[] buf = new byte[256];
            Visca.Splitter sp = new Visca.Splitter();
            while (running)
            {
                int n;
                try { n = stream.Read(buf, 0, buf.Length); }
                catch { return; }
                if (n <= 0) return;
                foreach (byte[] m in sp.Feed(buf, n)) Raise(m);
            }
        }
    }

    public class SerialTransport : ViscaTransport
    {
        readonly string portName; readonly int baud;
        SerialPort sp;
        readonly Visca.Splitter splitter = new Visca.Splitter();

        public SerialTransport(string portName, int baud) { this.portName = portName; this.baud = baud; }

        public override string Describe { get { return "VISCA serial " + portName + " @ " + baud; } }
        public override bool IsSerial { get { return true; } }

        public override void Open()
        {
            sp = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
            sp.ReadTimeout = 500;
            sp.DataReceived += delegate
            {
                try
                {
                    SerialPort p = sp;
                    if (p == null) return;
                    int n = p.BytesToRead;
                    byte[] buf = new byte[n];
                    n = p.Read(buf, 0, n);
                    foreach (byte[] m in splitter.Feed(buf, n)) Raise(m);
                }
                catch { }
            };
            sp.Open();
        }

        public override void Close()
        {
            if (sp != null) { sp.Close(); sp = null; }
        }

        public override void Send(byte[] msg, bool inquiry)
        {
            SerialPort p = sp;
            if (p == null) throw new InvalidOperationException("Not connected");
            lock (this) p.Write(msg, 0, msg.Length);
        }
    }

    // ---- camera ---------------------------------------------------------------

    /// <summary>Any VISCA camera (Sony, PTZOptics, Lumens, BirdDog, Marshall, AVer, ...).</summary>
    public class ViscaCamera : PtzCamera
    {
        public readonly ViscaProtocol Protocol;
        public readonly string Host;   // host name / IP, or COM port for serial
        public readonly int Port;      // network port, or baud for serial
        public readonly int Address;   // VISCA camera address 1..7
        readonly string label;

        ViscaTransport transport;
        readonly object inquiryLock = new object();
        readonly object stateLock = new object();
        readonly Queue<byte[]> replies = new Queue<byte[]>();
        readonly AutoResetEvent replyEvent = new AutoResetEvent(false);
        Thread poller;
        volatile bool polling;
        volatile int misses;
        volatile string lastError;
        PtzState state = new PtzState();
        DateTime fastPollUntil = DateTime.MinValue;

        public ViscaCamera(string label, ViscaProtocol protocol, string host, int port, int address)
        {
            this.label = label;
            Protocol = protocol; Host = host; Port = port; Address = Math.Max(1, Math.Min(7, address));
            // Typical Sony/PTZOptics limits; widened automatically if the camera reports more.
            PanRange = new AxisRange(-2448, 2448, 0, true);
            TiltRange = new AxisRange(-432, 1296, 0, true);
            ZoomRange = new AxisRange(0, 16384, 0, true);
            FocusRange = new AxisRange(4096, 49152, 4096, true);
            SupportsAutoFocus = true;
            state.AutoFocus = true;
        }

        public override string Name { get { return label; } }
        public override string Id { get { return "visca:" + Protocol + ":" + Host + ":" + Port + ":" + Address; } }
        public override string Kind { get { return transport != null ? transport.Describe : Visca.ProtocolName(Protocol); } }

        public override string Problem
        {
            get
            {
                if (misses >= 3) return "Camera not responding";
                return lastError;
            }
        }

        byte Head { get { return (byte)(0x80 | Address); } }

        public override void Connect()
        {
            switch (Protocol)
            {
                case ViscaProtocol.SonyUdp: transport = new UdpTransport(Host, Port, true); break;
                case ViscaProtocol.RawUdp: transport = new UdpTransport(Host, Port, false); break;
                case ViscaProtocol.RawTcp: transport = new TcpTransport(Host, Port); break;
                default: transport = new SerialTransport(Host, Port); break;
            }
            transport.MessageReceived += OnMessage;
            transport.Open();
            if (transport.IsSerial)
            {
                transport.Send(new byte[] { 0x88, 0x30, 0x01, 0xFF }, false);       // address set (broadcast)
                Thread.Sleep(100);
                transport.Send(new byte[] { 0x88, 0x01, 0x00, 0x01, 0xFF }, false); // IF_Clear (broadcast)
                Thread.Sleep(100);
            }
            // a few attempts: the first UDP packet to a new IP can be lost while ARP resolves
            bool answered = false;
            for (int attempt = 0; attempt < 3 && !answered; attempt++) answered = PollOnce();
            if (!answered)
            {
                transport.Dispose();
                transport = null;
                throw new Exception("No reply from the camera at " + Host + (Protocol == ViscaProtocol.Serial ? "" : ":" + Port) +
                    ".\nCheck the IP address/port, the protocol (Sony UDP / raw TCP / raw UDP) and that VISCA control is enabled on the camera.");
            }
            polling = true;
            poller = new Thread(PollLoop) { IsBackground = true, Name = "VISCA poller" };
            poller.Start();
        }

        public override void Disconnect()
        {
            polling = false;
            if (transport != null)
            {
                try { Send(0x01, 0x06, 0x01, 0x01, 0x01, 0x03, 0x03); } catch { } // stop pan/tilt
                transport.Dispose();
                transport = null;
            }
        }

        void OnMessage(byte[] m)
        {
            if (m.Length < 3) return;
            int kind = m[1] & 0xF0;
            if (m[1] == 0x50 && m.Length >= 4)
            {
                lock (replies) replies.Enqueue(m);
                replyEvent.Set();
            }
            else if (kind == 0x60 && m.Length >= 4)
            {
                switch (m[2])
                {
                    case 0x02: lastError = "VISCA syntax error"; break;
                    case 0x03: lastError = "Camera command buffer full"; break;
                    case 0x41: lastError = "Command not executable now"; break;
                    default: lastError = "VISCA error 0x" + m[2].ToString("X2"); break;
                }
            }
        }

        void Send(params byte[] body)
        {
            ViscaTransport t = transport;
            if (t == null) return;
            byte[] msg = new byte[body.Length + 2];
            msg[0] = Head;
            Buffer.BlockCopy(body, 0, msg, 1, body.Length);
            msg[msg.Length - 1] = 0xFF;
            try { t.Send(msg, false); lastError = null; }
            catch (Exception ex) { lastError = ex.Message; }
            fastPollUntil = DateTime.UtcNow.AddSeconds(3);
        }

        /// <summary>Sends an inquiry and waits for a "y0 50 ... FF" reply of the expected length.</summary>
        byte[] Inquire(int expectedLength, params byte[] body)
        {
            ViscaTransport t = transport;
            if (t == null) return null;
            byte[] msg = new byte[body.Length + 3];
            msg[0] = Head; msg[1] = 0x09;
            Buffer.BlockCopy(body, 0, msg, 2, body.Length);
            msg[msg.Length - 1] = 0xFF;
            lock (inquiryLock)
            {
                lock (replies) replies.Clear();
                replyEvent.Reset();
                try { t.Send(msg, true); }
                catch (Exception ex) { lastError = ex.Message; return null; }
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(400);
                while (true)
                {
                    lock (replies)
                    {
                        while (replies.Count > 0)
                        {
                            byte[] r = replies.Dequeue();
                            if (r.Length == expectedLength) { misses = 0; return r; }
                        }
                    }
                    int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) { misses++; return null; }
                    replyEvent.WaitOne(left);
                }
            }
        }

        /// <summary>Reads pan/tilt, zoom, focus and AF mode. Returns false if the camera did not answer.</summary>
        bool PollOnce()
        {
            byte[] pt = Inquire(11, 0x06, 0x12);
            if (pt == null) return false;
            byte[] z = Inquire(7, 0x04, 0x47);
            byte[] f = Inquire(7, 0x04, 0x48);
            byte[] af = Inquire(4, 0x04, 0x38);
            lock (stateLock)
            {
                state.Pan = Visca.ReadNibbles(pt, 2, true);
                state.Tilt = Visca.ReadNibbles(pt, 6, true);
                PanRange.Include(state.Pan);
                TiltRange.Include(state.Tilt);
                if (z != null) { state.Zoom = Visca.ReadNibbles(z, 2, false); ZoomRange.Include(state.Zoom); }
                if (f != null) { state.Focus = Visca.ReadNibbles(f, 2, false); FocusRange.Include(state.Focus); }
                if (af != null) state.AutoFocus = af[2] == 0x02;
            }
            return true;
        }

        void PollLoop()
        {
            while (polling)
            {
                try { PollOnce(); } catch { }
                Thread.Sleep(DateTime.UtcNow < fastPollUntil ? 120 : 500);
            }
        }

        public override PtzState State { get { lock (stateLock) return state.Clone(); } }

        public override PtzState ReadState()
        {
            PollOnce();
            return State;
        }

        // ---- commands -------------------------------------------------------------

        static byte Scale(double speed, int max)
        {
            speed = Math.Max(0, Math.Min(1, speed));
            return (byte)Math.Max(1, Math.Min(max, 1 + (int)Math.Round(speed * (max - 1))));
        }

        public override void DrivePanTilt(int panDir, int tiltDir, double speed)
        {
            byte x = (byte)(panDir < 0 ? 0x01 : panDir > 0 ? 0x02 : 0x03);
            byte y = (byte)(tiltDir > 0 ? 0x01 : tiltDir < 0 ? 0x02 : 0x03);
            Send(0x01, 0x06, 0x01, Scale(speed, 0x18), Scale(speed, 0x14), x, y);
        }

        public override void DriveZoom(int dir, double speed)
        {
            byte p = (byte)Math.Round(Math.Max(0, Math.Min(1, speed)) * 7);
            Send(0x01, 0x04, 0x07, (byte)(dir > 0 ? 0x20 | p : dir < 0 ? 0x30 | p : 0x00));
        }

        public override void DriveFocus(int dir, double speed)
        {
            byte p = (byte)Math.Round(Math.Max(0, Math.Min(1, speed)) * 7);
            // 2p = far, 3p = near
            Send(0x01, 0x04, 0x08, (byte)(dir > 0 ? 0x30 | p : dir < 0 ? 0x20 | p : 0x00));
        }

        public override void GoTo(int? pan, int? tilt, int? zoom, int? focus)
        {
            if (pan.HasValue || tilt.HasValue)
            {
                PtzState s = State;
                List<byte> b = new List<byte> { 0x01, 0x06, 0x02, 0x18, 0x14 };
                Visca.PutNibbles(b, pan.HasValue ? pan.Value : s.Pan);
                Visca.PutNibbles(b, tilt.HasValue ? tilt.Value : s.Tilt);
                Send(b.ToArray());
            }
            if (zoom.HasValue)
            {
                List<byte> b = new List<byte> { 0x01, 0x04, 0x47 };
                Visca.PutNibbles(b, zoom.Value);
                Send(b.ToArray());
            }
            if (focus.HasValue)
            {
                List<byte> b = new List<byte> { 0x01, 0x04, 0x48 };
                Visca.PutNibbles(b, focus.Value);
                Send(b.ToArray());
            }
        }

        public override void SetAutoFocus(bool on)
        {
            Send(0x01, 0x04, 0x38, (byte)(on ? 0x02 : 0x03));
            lock (stateLock) state.AutoFocus = on;
        }

        public override void Home()
        {
            Send(0x01, 0x06, 0x04);
        }
    }
}
