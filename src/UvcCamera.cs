using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;

namespace PTZVC
{
    // ---- minimal DirectShow COM interop ------------------------------------------------

    static class DsGuids
    {
        public static readonly Guid CLSID_SystemDeviceEnum = new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
        public static readonly Guid CLSID_FilterGraph = new Guid("E436EBB3-524F-11CE-9F53-0020AF0BA770");
        public static readonly Guid CLSID_CaptureGraphBuilder2 = new Guid("BF87B6E1-8C27-11d0-B3F0-00AA003761C5");
        public static readonly Guid IID_IBaseFilter = new Guid("56a86895-0ad4-11ce-b03a-0020af0ba770");
        public static readonly Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
        public static readonly Guid PIN_CATEGORY_PREVIEW = new Guid("fb6c4282-0353-11d1-905f-0000c0cc16ba");
        public static readonly Guid PIN_CATEGORY_CAPTURE = new Guid("fb6c4281-0353-11d1-905f-0000c0cc16ba");
        public static readonly Guid MEDIATYPE_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
    }

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid clsidDeviceClass, out IEnumMoniker ppEnumMoniker, int dwFlags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [MarshalAs(UnmanagedType.Struct)] out object pVar, IntPtr pErrorLog);
        [PreserveSig] int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, MarshalAs(UnmanagedType.Struct)] ref object pVar);
    }

    [ComImport, Guid("56a86895-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBaseFilter
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int Stop();
        [PreserveSig] int Pause();
        [PreserveSig] int Run(long tStart);
        [PreserveSig] int GetState(int dwMilliSecsTimeout, out int filtState);
        [PreserveSig] int SetSyncSource(IntPtr pClock);
        [PreserveSig] int GetSyncSource(out IntPtr pClock);
        [PreserveSig] int EnumPins(out IntPtr ppEnum);
        [PreserveSig] int FindPin([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr ppPin);
        [PreserveSig] int QueryFilterInfo(IntPtr pInfo);
        [PreserveSig] int JoinFilterGraph(IntPtr pGraph, [MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int QueryVendorInfo(out IntPtr pVendorInfo);
    }

    [ComImport, Guid("56a868a9-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphBuilder
    {
        [PreserveSig] int AddFilter([In] IBaseFilter pFilter, [In, MarshalAs(UnmanagedType.LPWStr)] string pName);
        [PreserveSig] int RemoveFilter([In] IBaseFilter pFilter);
    }

    [ComImport, Guid("93E5A4E0-2D50-11d2-ABFA-00A0C9C6E38D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICaptureGraphBuilder2
    {
        [PreserveSig] int SetFiltergraph([In] IGraphBuilder pfg);
        [PreserveSig] int GetFiltergraph(out IGraphBuilder ppfg);
        [PreserveSig] int SetOutputFileName(IntPtr pType, IntPtr lpstrFile, out IntPtr ppbf, out IntPtr ppSink);
        [PreserveSig] int FindInterface(IntPtr pCategory, IntPtr pType, IBaseFilter pbf, IntPtr riid, out IntPtr ppint);
        [PreserveSig] int RenderStream([In] ref Guid pCategory, [In] ref Guid pType, [In, MarshalAs(UnmanagedType.IUnknown)] object pSource, [In] IBaseFilter pfCompressor, [In] IBaseFilter pfRenderer);
    }

    [ComImport, Guid("56a868b1-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface IMediaControl
    {
        [PreserveSig] int Run();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
    }

    [ComImport, Guid("56a868b4-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface IVideoWindow
    {
        [PreserveSig] int put_Caption([MarshalAs(UnmanagedType.BStr)] string caption);
        [PreserveSig] int get_Caption([MarshalAs(UnmanagedType.BStr)] out string caption);
        [PreserveSig] int put_WindowStyle(int windowStyle);
        [PreserveSig] int get_WindowStyle(out int windowStyle);
        [PreserveSig] int put_WindowStyleEx(int windowStyleEx);
        [PreserveSig] int get_WindowStyleEx(out int windowStyleEx);
        [PreserveSig] int put_AutoShow(int autoShow);
        [PreserveSig] int get_AutoShow(out int autoShow);
        [PreserveSig] int put_WindowState(int windowState);
        [PreserveSig] int get_WindowState(out int windowState);
        [PreserveSig] int put_BackgroundPalette(int backgroundPalette);
        [PreserveSig] int get_BackgroundPalette(out int backgroundPalette);
        [PreserveSig] int put_Visible(int visible);
        [PreserveSig] int get_Visible(out int visible);
        [PreserveSig] int put_Left(int left);
        [PreserveSig] int get_Left(out int left);
        [PreserveSig] int put_Width(int width);
        [PreserveSig] int get_Width(out int width);
        [PreserveSig] int put_Top(int top);
        [PreserveSig] int get_Top(out int top);
        [PreserveSig] int put_Height(int height);
        [PreserveSig] int get_Height(out int height);
        [PreserveSig] int put_Owner(IntPtr owner);
        [PreserveSig] int get_Owner(out IntPtr owner);
        [PreserveSig] int put_MessageDrain(IntPtr drain);
        [PreserveSig] int get_MessageDrain(out IntPtr drain);
        [PreserveSig] int get_BorderColor(out int color);
        [PreserveSig] int put_BorderColor(int color);
        [PreserveSig] int get_FullScreenMode(out int fullScreenMode);
        [PreserveSig] int put_FullScreenMode(int fullScreenMode);
        [PreserveSig] int SetWindowForeground(int focus);
        [PreserveSig] int NotifyOwnerMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int SetWindowPosition(int left, int top, int width, int height);
    }

    [ComImport, Guid("56a868b5-0ad4-11ce-b03a-0020af0ba770"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface IBasicVideo
    {
        [PreserveSig] int get_AvgTimePerFrame(out double pAvgTimePerFrame);
        [PreserveSig] int get_BitRate(out int pBitRate);
        [PreserveSig] int get_BitErrorRate(out int pBitErrorRate);
        [PreserveSig] int get_VideoWidth(out int pVideoWidth);
        [PreserveSig] int get_VideoHeight(out int pVideoHeight);
        [PreserveSig] int put_SourceLeft(int v);
        [PreserveSig] int get_SourceLeft(out int v);
        [PreserveSig] int put_SourceWidth(int v);
        [PreserveSig] int get_SourceWidth(out int v);
        [PreserveSig] int put_SourceTop(int v);
        [PreserveSig] int get_SourceTop(out int v);
        [PreserveSig] int put_SourceHeight(int v);
        [PreserveSig] int get_SourceHeight(out int v);
        [PreserveSig] int put_DestinationLeft(int v);
        [PreserveSig] int get_DestinationLeft(out int v);
        [PreserveSig] int put_DestinationWidth(int v);
        [PreserveSig] int get_DestinationWidth(out int v);
        [PreserveSig] int put_DestinationTop(int v);
        [PreserveSig] int get_DestinationTop(out int v);
        [PreserveSig] int put_DestinationHeight(int v);
        [PreserveSig] int get_DestinationHeight(out int v);
        [PreserveSig] int SetSourcePosition(int left, int top, int width, int height);
        [PreserveSig] int GetSourcePosition(out int left, out int top, out int width, out int height);
        [PreserveSig] int SetDefaultSourcePosition();
        [PreserveSig] int SetDestinationPosition(int left, int top, int width, int height);
        [PreserveSig] int GetDestinationPosition(out int left, out int top, out int width, out int height);
        [PreserveSig] int SetDefaultDestinationPosition();
        [PreserveSig] int GetVideoSize(out int width, out int height);
        [PreserveSig] int GetVideoPaletteEntries(int startIndex, int entries, out int retrieved, IntPtr palette);
        [PreserveSig] int GetCurrentImage(ref int bufferSize, IntPtr dibImage);
    }

    /// <summary>Converts a packed DIB (BITMAPINFOHEADER + pixels, as GetCurrentImage returns) to a Bitmap.</summary>
    public static class Dib
    {
        public static Bitmap ToBitmap(byte[] dib)
        {
            if (dib == null || dib.Length < 40) throw new ArgumentException("Frame data is too short");
            int header = BitConverter.ToInt32(dib, 0);
            int w = BitConverter.ToInt32(dib, 4), h = BitConverter.ToInt32(dib, 8);
            int bpp = BitConverter.ToInt16(dib, 14), compression = BitConverter.ToInt32(dib, 16);
            int colorsUsed = BitConverter.ToInt32(dib, 32);
            const int BI_RGB = 0, BI_BITFIELDS = 3;
            if ((bpp != 24 && bpp != 32) || (compression != BI_RGB && compression != BI_BITFIELDS))
                throw new NotSupportedException("Unsupported frame format (" + bpp + "-bit, compression " + compression + ")");
            int offset = header + (compression == BI_BITFIELDS && header == 40 ? 12 : 0) + colorsUsed * 4;
            bool bottomUp = h > 0;
            h = Math.Abs(h);
            int stride = (w * bpp + 31) / 32 * 4;
            if (w <= 0 || h == 0 || offset + (long)stride * h > dib.Length) throw new ArgumentException("Frame data is truncated");

            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            byte[] row = new byte[w * 3];
            for (int y = 0; y < h; y++)
            {
                int src = offset + (bottomUp ? h - 1 - y : y) * stride;
                if (bpp == 24) Buffer.BlockCopy(dib, src, row, 0, row.Length);
                else
                    for (int x = 0; x < w; x++)
                    {
                        row[x * 3] = dib[src + x * 4];
                        row[x * 3 + 1] = dib[src + x * 4 + 1];
                        row[x * 3 + 2] = dib[src + x * 4 + 2];
                    }
                Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row.Length);
            }
            bmp.UnlockBits(bd);
            return bmp;
        }
    }

    [ComImport, Guid("C6E13370-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAMCameraControl
    {
        [PreserveSig] int GetRange(int property, out int pMin, out int pMax, out int pSteppingDelta, out int pDefault, out int pCapsFlags);
        [PreserveSig] int Set(int property, int lValue, int flags);
        [PreserveSig] int Get(int property, out int lValue, out int flags);
    }

    // ---- device enumeration -------------------------------------------------------------

    public class UvcDevice
    {
        public string Name;
        public string Path;
        internal IMoniker Moniker;
        public override string ToString() { return Name; }
    }

    public static class UvcDevices
    {
        public static List<UvcDevice> List()
        {
            List<UvcDevice> list = new List<UvcDevice>();
            try
            {
                ICreateDevEnum devEnum = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(DsGuids.CLSID_SystemDeviceEnum));
                Guid cat = DsGuids.CLSID_VideoInputDeviceCategory;
                IEnumMoniker em;
                if (devEnum.CreateClassEnumerator(ref cat, out em, 0) != 0 || em == null) return list;
                IMoniker[] one = new IMoniker[1];
                while (em.Next(1, one, IntPtr.Zero) == 0)
                {
                    UvcDevice d = new UvcDevice { Moniker = one[0] };
                    d.Name = ReadProp(one[0], "FriendlyName") ?? "Video device";
                    d.Path = ReadProp(one[0], "DevicePath") ?? d.Name;
                    list.Add(d);
                }
                Marshal.ReleaseComObject(em);
                Marshal.ReleaseComObject(devEnum);
            }
            catch { }
            return list;
        }

        static string ReadProp(IMoniker m, string name)
        {
            object bagObj = null;
            try
            {
                Guid iid = DsGuids.IID_IPropertyBag;
                m.BindToStorage(null, null, ref iid, out bagObj);
                IPropertyBag bag = (IPropertyBag)bagObj;
                object val;
                if (bag.Read(name, out val, IntPtr.Zero) == 0) return val as string;
            }
            catch { }
            finally { if (bagObj != null) Marshal.ReleaseComObject(bagObj); }
            return null;
        }
    }

    // ---- camera -------------------------------------------------------------------------

    /// <summary>USB (UVC) PTZ camera controlled through DirectShow IAMCameraControl.</summary>
    public class UvcCamera : PtzCamera
    {
        const int PropPan = 0, PropTilt = 1, PropZoom = 3, PropFocus = 6;
        const int FlagAuto = 1, FlagManual = 2;

        readonly UvcDevice device;
        IBaseFilter filter;
        IAMCameraControl cc;
        IGraphBuilder graph;
        ICaptureGraphBuilder2 builder;
        IMediaControl mediaControl;
        IVideoWindow videoWindow;
        Rectangle videoRect;
        Control previewHost;
        Label previewMessage;

        PtzState state = new PtzState();
        readonly Jogger jog = new Jogger();
        double panPos, tiltPos, zoomPos, focusPos; // fractional positions while jogging
        double refreshTimer;
        string problem;

        public UvcCamera(UvcDevice device)
        {
            this.device = device;
            FocusIncreasesNear = false; // UVC focus is a distance: larger = farther
        }

        public override string Name { get { return device.Name; } }
        public override string Id { get { return "uvc:" + device.Path; } }
        public override string Kind { get { return "USB / UVC (DirectShow)"; } }
        public override string Problem { get { return problem; } }

        public override string Format(Axis axis, int v)
        {
            // DirectShow reports pan/tilt in degrees; zoom/focus units are device specific
            if (axis == Axis.Pan || axis == Axis.Tilt) return v + "°";
            return v.ToString();
        }

        public override void Connect()
        {
            object o;
            Guid iid = DsGuids.IID_IBaseFilter;
            device.Moniker.BindToObject(null, null, ref iid, out o);
            filter = (IBaseFilter)o;
            cc = o as IAMCameraControl;
            if (cc != null)
            {
                PanRange = Range(PropPan);
                TiltRange = Range(PropTilt);
                ZoomRange = Range(PropZoom);
                FocusRange = Range(PropFocus);
                int min, max, step, def, caps;
                SupportsAutoFocus = cc.GetRange(PropFocus, out min, out max, out step, out def, out caps) == 0 && (caps & FlagAuto) != 0;
            }
            else
            {
                PanRange.Supported = TiltRange.Supported = ZoomRange.Supported = FocusRange.Supported = false;
            }
            Refresh();
            panPos = state.Pan; tiltPos = state.Tilt; zoomPos = state.Zoom; focusPos = state.Focus;
            if (!PanRange.Supported && !TiltRange.Supported && !ZoomRange.Supported && !FocusRange.Supported)
                problem = "This camera reports no pan/tilt/zoom/focus controls";
        }

        AxisRange Range(int prop)
        {
            int min, max, step, def, caps;
            if (cc.GetRange(prop, out min, out max, out step, out def, out caps) == 0 && max > min)
                return new AxisRange(min, max, def, true);
            return new AxisRange(0, 1, 0, false);
        }

        void Refresh()
        {
            if (cc == null) return;
            int v, flags;
            if (PanRange.Supported && cc.Get(PropPan, out v, out flags) == 0) state.Pan = v;
            if (TiltRange.Supported && cc.Get(PropTilt, out v, out flags) == 0) state.Tilt = v;
            if (ZoomRange.Supported && cc.Get(PropZoom, out v, out flags) == 0) state.Zoom = v;
            if (FocusRange.Supported && cc.Get(PropFocus, out v, out flags) == 0)
            {
                state.Focus = v;
                state.AutoFocus = (flags & FlagAuto) != 0;
            }
        }

        void Set(int prop, AxisRange r, double value)
        {
            if (cc == null || !r.Supported) return;
            int hr = cc.Set(prop, r.Clamp(value), FlagManual);
            problem = hr == 0 ? null : "Camera rejected a command (0x" + hr.ToString("X8") + ")";
        }

        public override void Disconnect()
        {
            DetachPreview();
            if (filter != null) { Marshal.ReleaseComObject(filter); filter = null; cc = null; }
        }

        public override void DrivePanTilt(int panDir, int tiltDir, double speed)
        {
            if (!jog.Active) { panPos = state.Pan; tiltPos = state.Tilt; }
            jog.PanDir = panDir; jog.TiltDir = tiltDir; jog.PanTiltSpeed = speed;
        }

        public override void DriveZoom(int dir, double speed)
        {
            if (!jog.Active) zoomPos = state.Zoom;
            jog.ZoomDir = dir; jog.ZoomSpeed = speed;
        }

        public override void DriveFocus(int dir, double speed)
        {
            if (!jog.Active) focusPos = state.Focus;
            jog.FocusDir = dir; jog.FocusSpeed = speed;
        }

        public override void Tick(double dt)
        {
            if (cc == null) return;
            if (jog.Active)
            {
                if (jog.PanDir != 0)
                {
                    panPos = Step(panPos, jog.PanDir, PanRange, Jogger.SweepTime(jog.PanTiltSpeed, 3, 60), dt);
                    Set(PropPan, PanRange, panPos); state.Pan = PanRange.Clamp(panPos);
                }
                if (jog.TiltDir != 0)
                {
                    tiltPos = Step(tiltPos, jog.TiltDir, TiltRange, Jogger.SweepTime(jog.PanTiltSpeed, 3, 60), dt);
                    Set(PropTilt, TiltRange, tiltPos); state.Tilt = TiltRange.Clamp(tiltPos);
                }
                if (jog.ZoomDir != 0)
                {
                    zoomPos = Step(zoomPos, jog.ZoomDir, ZoomRange, Jogger.SweepTime(jog.ZoomSpeed, 3, 30), dt);
                    Set(PropZoom, ZoomRange, zoomPos); state.Zoom = ZoomRange.Clamp(zoomPos);
                }
                if (jog.FocusDir != 0)
                {
                    // +1 = near = smaller focus distance for UVC
                    focusPos = Step(focusPos, -jog.FocusDir, FocusRange, Jogger.SweepTime(jog.FocusSpeed, 3, 30), dt);
                    Set(PropFocus, FocusRange, focusPos); state.Focus = FocusRange.Clamp(focusPos);
                }
                return;
            }
            refreshTimer += dt;
            if (refreshTimer > 0.3) { refreshTimer = 0; Refresh(); }
        }

        static double Step(double pos, int dir, AxisRange r, double sweepSeconds, double dt)
        {
            double delta = dir * r.Span / sweepSeconds * dt;
            return Math.Max(r.Min, Math.Min(r.Max, pos + delta));
        }

        public override void GoTo(int? pan, int? tilt, int? zoom, int? focus)
        {
            if (pan.HasValue) { Set(PropPan, PanRange, pan.Value); state.Pan = PanRange.Clamp(pan.Value); }
            if (tilt.HasValue) { Set(PropTilt, TiltRange, tilt.Value); state.Tilt = TiltRange.Clamp(tilt.Value); }
            if (zoom.HasValue) { Set(PropZoom, ZoomRange, zoom.Value); state.Zoom = ZoomRange.Clamp(zoom.Value); }
            if (focus.HasValue) { Set(PropFocus, FocusRange, focus.Value); state.Focus = FocusRange.Clamp(focus.Value); }
            panPos = state.Pan; tiltPos = state.Tilt; zoomPos = state.Zoom; focusPos = state.Focus;
        }

        public override void SetAutoFocus(bool on)
        {
            if (cc == null || !FocusRange.Supported) return;
            int v, flags;
            if (cc.Get(PropFocus, out v, out flags) != 0) v = state.Focus;
            cc.Set(PropFocus, v, on ? FlagAuto : FlagManual);
            state.AutoFocus = on;
        }

        public override PtzState State { get { return state.Clone(); } }

        public override PtzState ReadState()
        {
            if (!jog.Active) Refresh();
            return state.Clone();
        }

        // ---- live preview ----------------------------------------------------------------

        public override bool AttachPreview(Control host)
        {
            previewHost = host;
            try
            {
                graph = (IGraphBuilder)Activator.CreateInstance(Type.GetTypeFromCLSID(DsGuids.CLSID_FilterGraph));
                builder = (ICaptureGraphBuilder2)Activator.CreateInstance(Type.GetTypeFromCLSID(DsGuids.CLSID_CaptureGraphBuilder2));
                Check(builder.SetFiltergraph(graph), "SetFiltergraph");
                Check(graph.AddFilter(filter, "Camera"), "AddFilter");
                Guid cat = DsGuids.PIN_CATEGORY_PREVIEW, type = DsGuids.MEDIATYPE_Video;
                if (builder.RenderStream(ref cat, ref type, filter, null, null) < 0)
                {
                    cat = DsGuids.PIN_CATEGORY_CAPTURE;
                    Check(builder.RenderStream(ref cat, ref type, filter, null, null), "RenderStream");
                }
                videoWindow = (IVideoWindow)graph;
                mediaControl = (IMediaControl)graph;
                videoWindow.put_Owner(host.Handle);
                videoWindow.put_WindowStyle(0x40000000 | 0x04000000 | 0x02000000); // WS_CHILD | WS_CLIPSIBLINGS | WS_CLIPCHILDREN
                videoWindow.put_MessageDrain(host.Handle);
                host.Resize += HostResize;
                HostResize(null, EventArgs.Empty);
                videoWindow.put_Visible(-1);
                Check(mediaControl.Run(), "Run");
                return true;
            }
            catch (Exception ex)
            {
                ReleaseGraph();
                previewMessage = new Label
                {
                    Text = "Live preview unavailable for this camera.\n" + ex.Message + "\n\nPTZ control still works if the camera supports it.\n(Close other apps that may be using the camera.)",
                    ForeColor = Theme.TextDim, BackColor = Color.Black, Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter, Font = Theme.Normal
                };
                host.Controls.Add(previewMessage);
                return true;
            }
        }

        static void Check(int hr, string what)
        {
            if (hr < 0) throw new COMException(what + " failed (0x" + hr.ToString("X8") + ")", hr);
        }

        void HostResize(object sender, EventArgs e)
        {
            if (videoWindow == null || previewHost == null) return;
            // keep 16:9 inside the host
            int w = previewHost.ClientSize.Width, h = previewHost.ClientSize.Height;
            int fw = w, fh = w * 9 / 16;
            if (fh > h) { fh = h; fw = h * 16 / 9; }
            videoRect = new Rectangle((w - fw) / 2, (h - fh) / 2, fw, fh);
            videoWindow.SetWindowPosition(videoRect.X, videoRect.Y, videoRect.Width, videoRect.Height);
        }

        // ---- snapshot ----------------------------------------------------------------------

        public override bool CanSnapshot { get { return graph != null; } }

        public override Bitmap Snapshot()
        {
            if (graph == null) throw new InvalidOperationException("No live video from this camera (is another app using it?)");
            try
            {
                return CurrentImage();
            }
            catch (Exception ex)
            {
                // Some renderers refuse GetCurrentImage; copy the picture off the screen instead.
                Form form = previewHost == null ? null : previewHost.FindForm();
                if (form == null || form.WindowState == FormWindowState.Minimized || videoRect.Width < 16)
                    throw new InvalidOperationException("Could not grab a frame (" + ex.Message + ")");
                Rectangle r = previewHost.RectangleToScreen(videoRect);
                Bitmap shot = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                return shot;
            }
        }

        /// <summary>The frame waiting at the video renderer, at the camera's full resolution.</summary>
        Bitmap CurrentImage()
        {
            IBasicVideo video = (IBasicVideo)graph;
            int size = 0;
            Check(video.GetCurrentImage(ref size, IntPtr.Zero), "GetCurrentImage");
            if (size <= 40) throw new COMException("GetCurrentImage returned no data");
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                Check(video.GetCurrentImage(ref size, mem), "GetCurrentImage");
                byte[] dib = new byte[size];
                Marshal.Copy(mem, dib, 0, size);
                return Dib.ToBitmap(dib);
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        void ReleaseGraph()
        {
            if (mediaControl != null) { try { mediaControl.Stop(); } catch { } }
            if (videoWindow != null)
            {
                try { videoWindow.put_Visible(0); videoWindow.put_Owner(IntPtr.Zero); } catch { }
            }
            if (graph != null && filter != null) { try { graph.RemoveFilter(filter); } catch { } }
            if (builder != null) Marshal.ReleaseComObject(builder);
            if (graph != null) Marshal.ReleaseComObject(graph);
            builder = null; graph = null; mediaControl = null; videoWindow = null;
        }

        public override void DetachPreview()
        {
            if (previewHost != null) previewHost.Resize -= HostResize;
            ReleaseGraph();
            if (previewMessage != null)
            {
                if (previewMessage.Parent != null) previewMessage.Parent.Controls.Remove(previewMessage);
                previewMessage.Dispose();
                previewMessage = null;
            }
            previewHost = null;
        }
    }
}
