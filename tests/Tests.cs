using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace PTZVC.Tests
{
    static class Tests
    {
        static int passed, failed;

        static void Check(bool ok, string what)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + what); }
            else { failed++; Console.WriteLine("  FAIL  " + what); }
        }

        static bool Near(double a, double b, double tol) { return Math.Abs(a - b) <= tol; }

        static int Main(string[] args)
        {
            string outDir = args.Length > 0 && args[0].Length > 0 ? args[0] : null; // optional folder for rendered frames
            Run("VISCA nibble encoding", NibbleTests);
            Run("Simulator physics", SimTests);
            Run("Preset storage", PresetTests);
            Run("VISCA end-to-end: Sony VISCA over IP (UDP)", delegate { EndToEnd(ViscaProtocol.SonyUdp); });
            Run("VISCA end-to-end: raw TCP", delegate { EndToEnd(ViscaProtocol.RawTcp); });
            Run("VISCA end-to-end: raw UDP", delegate { EndToEnd(ViscaProtocol.RawUdp); });
            Run("VISCA no camera -> clean error", NoCameraTest);
            Run("Scene rendering", delegate { RenderTests(outDir); });
            Run("Snapshots and logos", delegate { SnapshotTests(outDir); });
            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ALL " + passed + " CHECKS PASSED" : failed + " FAILED, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }

        static void Run(string name, Action a)
        {
            Console.WriteLine(name);
            try { a(); }
            catch (Exception ex) { failed++; Console.WriteLine("  FAIL  exception: " + ex); }
        }

        static void NibbleTests()
        {
            foreach (int v in new int[] { 0, 1, -1, 2448, -2448, 1296, -432, 16384, 49152, 4096 })
            {
                List<byte> b = new List<byte>();
                Visca.PutNibbles(b, v);

                int back = Visca.ReadNibbles(b.ToArray(), 0, v < 0);
                Check(back == v && b.TrueForAll(x => x <= 0x0F), "round trip " + v + " -> " + Visca.Hex(b.ToArray()));
            }
            List<byte> neg = new List<byte>();
            Visca.PutNibbles(neg, -2448);
            Check(Visca.Hex(neg.ToArray()) == "0F 06 07 00", "-2448 encodes as 0F 06 07 00 (0xF670)");
        }

        static void SimTests()
        {
            SimModel m = new SimModel();
            m.DepthProbe = SimScene.DepthAt;
            m.SetPanTilt(1000, 500, 90, 70);
            m.SetZoom(8000);
            for (int i = 0; i < 200; i++) m.Step(0.05);
            PtzState s = m.Snapshot();
            Check(s.Pan == 1000 && s.Tilt == 500 && s.Zoom == 8000, "absolute move reaches target (" + s.Pan + "," + s.Tilt + "," + s.Zoom + ")");
            Check(!m.IsMoving, "motion stops at target");

            m.DrivePanTilt(1, 0, 80, 64);
            for (int i = 0; i < 400; i++) m.Step(0.05);
            Check(m.Snapshot().Pan == SimModel.PanMax, "continuous pan clamps at limit");
            m.DrivePanTilt(0, 0, 0, 0);

            // AF converges to subject distance under the crosshair (podium at 4 m, pan 0 tilt -5deg)
            m.SetPanTilt(0, (int)(-5 / SimModel.DegPerUnit), 200, 200);
            m.SetAutoFocus(true);
            for (int i = 0; i < 200; i++) m.Step(0.05);
            double ideal = SimModel.FocusUnitsForDistance(4);
            Check(Near(m.Snapshot().Focus, ideal, 3), "auto focus converges on the 4 m podium (" + m.Snapshot().Focus + " vs " + (int)ideal + ")");

            m.SetAutoFocus(false);
            m.SetFocus(SimModel.FocusMax);
            for (int i = 0; i < 100; i++) m.Step(0.05);
            Check(m.Snapshot().Focus == SimModel.FocusMax && !m.Snapshot().AutoFocus, "manual focus holds a set value");
            double blur = SimView.BlurPixels(SimModel.FocusMax, 0, -5, 1, 960);
            Check(blur > 10, "far-off focus produces visible blur (" + blur.ToString("0.0") + " px)");
            Check(SimView.BlurPixels(ideal, 0, -5, 1, 960) < 0.01, "correct focus is sharp");

            Check(Near(SimModel.ZoomFactor(0), 1, 1e-9) && Near(SimModel.ZoomFactor(SimModel.ZoomMax), 20, 1e-9), "zoom maps 1x..20x");
            Check(SimScene.DepthAt(60, 15) > 100, "window looks outdoors (infinite focus)");
        }

        static void PresetTests()
        {
            string file = Path.Combine(Path.GetTempPath(), "ptzvc-test-presets-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                PresetStore a = new PresetStore(file);
                a.Set("cam-A", 3, new Preset { Pan = -1200, Tilt = 345, Zoom = 9000, Focus = 20000, Saved = DateTime.Now });
                a.Set("cam-A", 12, new Preset { Pan = 1, Tilt = 2, Zoom = 3, Focus = 4, Saved = DateTime.Now });
                a.Set("uvc:\\\\?\\usb#vid_046d&pid_0853", 1, new Preset { Pan = 5, Tilt = -5, Zoom = 100, Focus = 30, Saved = DateTime.Now });
                PresetStore b = new PresetStore(file);
                Preset p = b.Get("cam-A", 3);
                Check(p != null && p.Pan == -1200 && p.Tilt == 345 && p.Zoom == 9000 && p.Focus == 20000, "preset survives restart");
                Check(b.Get("cam-B", 3) == null, "presets are per camera");
                Check(b.Get("uvc:\\\\?\\usb#vid_046d&pid_0853", 1) != null, "device-path camera ids round trip");
                b.Clear("cam-A", 12);
                Check(new PresetStore(file).Get("cam-A", 12) == null, "clear removes preset");
            }
            finally { File.Delete(file); }
        }

        static int portBase = 41000;

        static void EndToEnd(ViscaProtocol proto)
        {
            SimModel model = new SimModel();
            model.DepthProbe = SimScene.DepthAt;
            ViscaServer server = new ViscaServer(model);
            server.SonyUdpPort = portBase++; server.TcpPort = portBase++; server.RawUdpPort = portBase++;
            List<string> log = new List<string>();
            server.Log += delegate(string who, string hex, string meaning) { lock (log) log.Add(meaning); };
            server.Start();
            Check(server.Failed.Count == 0, "virtual camera listening (" + string.Join(", ", server.Listening.ToArray()) + ")");

            bool stepping = true;
            Thread physics = new Thread(delegate() { while (stepping) { model.Step(0.02); Thread.Sleep(20); } }) { IsBackground = true };
            physics.Start();

            int port = proto == ViscaProtocol.SonyUdp ? server.SonyUdpPort : proto == ViscaProtocol.RawTcp ? server.TcpPort : server.RawUdpPort;
            ViscaCamera cam = new ViscaCamera("test", proto, "127.0.0.1", port, 1);
            try
            {
                cam.Connect();
                Check(true, "connected over " + cam.Kind);

                cam.GoTo(-1200, 300, 6000, null);
                Thread.Sleep(2500);
                PtzState s = cam.ReadState();
                Check(s.Pan == -1200 && s.Tilt == 300 && s.Zoom == 6000, "absolute pan/tilt/zoom reached and read back (" + s.Pan + "," + s.Tilt + "," + s.Zoom + ")");

                cam.DrivePanTilt(1, 1, 0.5);
                Thread.Sleep(600);
                cam.DrivePanTilt(0, 0, 0.5);
                Thread.Sleep(200);
                PtzState d = cam.ReadState();
                Check(d.Pan > s.Pan && d.Tilt > s.Tilt, "continuous drive up-right moves the camera (" + d.Pan + "," + d.Tilt + ")");
                Thread.Sleep(300);
                Check(cam.ReadState().Pan == d.Pan, "stop command halts motion");

                cam.DriveZoom(1, 1);
                Thread.Sleep(400);
                cam.DriveZoom(0, 0);
                Thread.Sleep(150);
                Check(cam.ReadState().Zoom > s.Zoom, "zoom tele drive");

                cam.SetAutoFocus(false);
                Thread.Sleep(200);
                Check(!cam.ReadState().AutoFocus && !model.AutoFocus, "auto focus OFF reaches the camera");
                cam.GoTo(null, null, null, 30000);
                Thread.Sleep(1500);
                Check(Near(cam.ReadState().Focus, 30000, 1), "manual focus position applied (" + cam.ReadState().Focus + ")");
                cam.DriveFocus(-1, 0.5);
                Thread.Sleep(300);
                cam.DriveFocus(0, 0);
                Thread.Sleep(150);
                Check(cam.ReadState().Focus < 30000, "focus FAR drive");

                cam.SetAutoFocus(true);
                Thread.Sleep(200);
                Check(cam.ReadState().AutoFocus, "auto focus ON reaches the camera");

                // Preset recall semantics, as the UI does it: AF on -> focus not sent
                Preset preset = new Preset { Pan = 900, Tilt = -100, Zoom = 2000, Focus = 45000 };
                cam.GoTo(preset.Pan, preset.Tilt, preset.Zoom, null);
                Thread.Sleep(2500);
                PtzState r = cam.ReadState();
                Check(r.Pan == 900 && r.Tilt == -100 && r.Zoom == 2000, "preset recall restores pan/tilt/zoom");
                Check(r.Focus != 45000, "with auto focus on, saved focus is not applied (AF decides: " + r.Focus + ")");

                cam.Home();
                Thread.Sleep(2500);
                PtzState h = cam.ReadState();
                Check(h.Pan == 0 && h.Tilt == 0, "home/center returns to 0,0");
                Check(cam.Problem == null, "no protocol errors reported (" + (cam.Problem ?? "none") + ")");
                lock (log) Check(log.Exists(x => x.StartsWith("Pan-tilt drive")) && log.Exists(x => x.StartsWith("Zoom direct")), "server decoded real VISCA commands (" + log.Count + " messages)");
            }
            finally
            {
                cam.Dispose();
                stepping = false;
                server.Dispose();
            }
        }

        static void NoCameraTest()
        {
            ViscaCamera cam = new ViscaCamera("nobody", ViscaProtocol.SonyUdp, "127.0.0.1", 40999, 1);
            Stopwatch sw = Stopwatch.StartNew();
            try { cam.Connect(); Check(false, "connect should fail"); }
            catch (Exception ex) { Check(ex.Message.Contains("No reply"), "helpful error: " + ex.Message.Split('\n')[0]); }
            Check(sw.Elapsed.TotalSeconds < 3, "fails fast (" + sw.Elapsed.TotalSeconds.ToString("0.0") + " s)");
            cam.Dispose();

            ViscaCamera tcp = new ViscaCamera("nobody", ViscaProtocol.RawTcp, "127.0.0.1", 40998, 1);
            try { tcp.Connect(); Check(false, "tcp connect should fail"); }
            catch (Exception ex) { Check(true, "tcp refused: " + ex.GetType().Name); }
            tcp.Dispose();
        }

        static void RenderTests(string outDir)
        {
            double[][] shots =
            {
                new double[] { 0, 0, 0 },       // stage, wide
                new double[] { 17, 4.4, 16384 },  // fine print, 20x
                new double[] { 60, 5, 6000 },   // window
                new double[] { 120, 10, 7000 }, // color bars
                new double[] { 180, 0, 0 },     // back wall (seam)
                new double[] { -170, 5, 0 },    // across the seam
                new double[] { -60, 12, 6000 }, // whiteboard
                new double[] { -120, 8, 4000 }, // library
                new double[] { 30, 90, 0 },     // ceiling
                new double[] { 0, -30, 0 },     // floor
            };
            using (Bitmap bmp = new Bitmap(960, 540, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                SimScene.Render(g, 960, 540, 0, 0, 62); // warm-up
                Stopwatch sw = Stopwatch.StartNew();
                int n = 0;
                foreach (double[] s in shots)
                {
                    SimScene.Render(g, 960, 540, s[0], s[1], SimModel.Hfov(s[2]));
                    n++;
                    if (outDir != null && Directory.Exists(outDir))
                        bmp.Save(Path.Combine(outDir, string.Format("render_{0}_{1}_{2}.png", s[0], s[1], s[2])), ImageFormat.Png);
                }
                sw.Restart();
                for (int i = 0; i < 30; i++) SimScene.Render(g, 960, 540, i * 3, 2, SimModel.Hfov(i * 400));
                double ms = sw.Elapsed.TotalMilliseconds / 30;
                Check(ms < 30, "render speed " + ms.ToString("0.0") + " ms/frame at 960x540 (needs < 30)");
                Color c = bmp.GetPixel(480, 270);
                Check(c.A == 255, "frame is opaque");
            }
        }

        static void SnapshotTests(string outDir)
        {
            // packed DIBs as IBasicVideo.GetCurrentImage returns them: 3x2, red top-left, blue bottom-right
            foreach (int bpp in new int[] { 24, 32 })
                foreach (bool bottomUp in new bool[] { true, false })
                {
                    int w = 3, h = 2, stride = (w * bpp + 31) / 32 * 4;
                    byte[] dib = new byte[40 + stride * h];
                    BitConverter.GetBytes(40).CopyTo(dib, 0);
                    BitConverter.GetBytes(w).CopyTo(dib, 4);
                    BitConverter.GetBytes(bottomUp ? h : -h).CopyTo(dib, 8);
                    BitConverter.GetBytes((short)1).CopyTo(dib, 12);
                    BitConverter.GetBytes((short)bpp).CopyTo(dib, 14);
                    int top = 40 + (bottomUp ? 1 : 0) * stride, bottom = 40 + (bottomUp ? 0 : 1) * stride;
                    dib[top + 2] = 255;                                 // (0,0) red   (BGR order)
                    dib[bottom + (w - 1) * (bpp / 8)] = 255;            // (2,1) blue
                    using (Bitmap b = Dib.ToBitmap(dib))
                        Check(b.Width == 3 && b.Height == 2 && b.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && b.GetPixel(2, 1).ToArgb() == Color.Blue.ToArgb()
                              && b.GetPixel(1, 0).ToArgb() == Color.Black.ToArgb(),
                            "DIB " + bpp + "-bit " + (bottomUp ? "bottom-up" : "top-down") + " converts with correct orientation");
                }
            try { Dib.ToBitmap(new byte[60]); Check(false, "bad DIB should throw"); }
            catch (Exception ex) { Check(!(ex is NullReferenceException) && !(ex is IndexOutOfRangeException), "bad DIB rejected cleanly (" + ex.GetType().Name + ")"); }

            SimCamera cam = new SimCamera();
            cam.GoTo(0, 0, 0, null);
            cam.Tick(5);
            Check(cam.CanSnapshot, "virtual camera supports snapshots");
            using (Bitmap shot = cam.Snapshot())
            {
                Check(shot.Width == 1920 && shot.Height == 1080, "virtual camera snapshot is 1920x1080");
                HashSet<int> colors = new HashSet<int>();
                for (int x = 0; x < shot.Width; x += 97) for (int y = 0; y < shot.Height; y += 61) colors.Add(shot.GetPixel(x, y).ToArgb());
                Check(colors.Count > 20, "snapshot has picture content (" + colors.Count + " sampled colors)");
                if (outDir != null && Directory.Exists(outDir)) shot.Save(Path.Combine(outDir, "snapshot.png"), ImageFormat.Png);
            }
            Check(!new ViscaCamera("x", ViscaProtocol.SonyUdp, "127.0.0.1", 1, 1).CanSnapshot,
                "network cameras report no snapshot");

            string dir = Path.Combine(Path.GetTempPath(), "PTZVC-snap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                DateTime when = new DateTime(2026, 10, 17, 13, 5, 9);
                string f1 = MainForm.SnapshotFile(dir, "USB: Cam/1?", when);
                Check(Path.GetFileName(f1) == "USB_ Cam_1_ 2026-10-17 13-05-09.png", "snapshot file name is safe: " + Path.GetFileName(f1));
                File.WriteAllText(f1, "");
                string f2 = MainForm.SnapshotFile(dir, "USB: Cam/1?", when);
                Check(f2 != f1 && f2.EndsWith(" (2).png"), "same-second snapshots don't overwrite: " + Path.GetFileName(f2));
            }
            finally { Directory.Delete(dir, true); }

            foreach (string logo in new string[] { Brand.Horizontal, Brand.Vertical, Brand.Mark })
            {
                Bitmap b = Brand.Tinted(logo, Color.White, 40);
                Check(b != null && b.Height == 40 && b.Width > 10, "logo " + logo + " embedded (" + (b == null ? "missing" : b.Width + "x" + b.Height) + ")");
            }
        }
    }
}
