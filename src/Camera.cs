using System;
using System.Drawing;
using System.Windows.Forms;

namespace PTZVC
{
    public class AxisRange
    {
        public int Min;
        public int Max;
        public int Home;
        public bool Supported;

        public AxisRange(int min, int max, int home, bool supported)
        {
            Min = min; Max = max; Home = home; Supported = supported;
        }

        public int Clamp(double v)
        {
            if (v < Min) return Min;
            if (v > Max) return Max;
            return (int)Math.Round(v);
        }

        public double Span { get { return Math.Max(1, Max - Min); } }

        /// <summary>Grow the range when a device reports a value outside the assumed limits.</summary>
        public void Include(int v)
        {
            if (v < Min) Min = v;
            if (v > Max) Max = v;
        }
    }

    public enum Axis { Pan, Tilt, Zoom, Focus }

    public class PtzState
    {
        public int Pan, Tilt, Zoom, Focus;
        public bool AutoFocus;

        public PtzState Clone()
        {
            return (PtzState)MemberwiseClone();
        }
    }

    /// <summary>
    /// Common contract for every camera backend (virtual simulator, USB/UVC via DirectShow, VISCA).
    /// All methods are called from the UI thread.
    /// </summary>
    public abstract class PtzCamera : IDisposable
    {
        public AxisRange PanRange = new AxisRange(-100, 100, 0, false);
        public AxisRange TiltRange = new AxisRange(-100, 100, 0, false);
        public AxisRange ZoomRange = new AxisRange(0, 100, 0, false);
        public AxisRange FocusRange = new AxisRange(0, 100, 0, false);
        public bool SupportsAutoFocus;
        /// <summary>True when a larger focus value means a nearer focus distance (VISCA, simulator).</summary>
        public bool FocusIncreasesNear = true;

        /// <summary>Display name.</summary>
        public abstract string Name { get; }

        /// <summary>Stable key used to store presets for this camera.</summary>
        public abstract string Id { get; }

        /// <summary>Short description of the connection, e.g. "VISCA UDP 192.168.1.20:52381".</summary>
        public virtual string Kind { get { return ""; } }

        /// <summary>Human-readable value for the readout bar.</summary>
        public virtual string Format(Axis axis, int value) { return value.ToString(); }

        /// <summary>Non-null when the camera is in trouble (not responding, command errors...).</summary>
        public virtual string Problem { get { return null; } }

        public abstract void Connect();
        public abstract void Disconnect();

        /// <summary>Continuous pan/tilt. dir = -1, 0, +1 (pan: -1 left; tilt: +1 up). speed 0..1. (0,0) stops.</summary>
        public abstract void DrivePanTilt(int panDir, int tiltDir, double speed);

        /// <summary>Continuous zoom. +1 tele (in), -1 wide (out), 0 stop.</summary>
        public abstract void DriveZoom(int dir, double speed);

        /// <summary>Continuous focus. +1 near, -1 far, 0 stop.</summary>
        public abstract void DriveFocus(int dir, double speed);

        /// <summary>Absolute move. Null values are left unchanged.</summary>
        public abstract void GoTo(int? pan, int? tilt, int? zoom, int? focus);

        public abstract void SetAutoFocus(bool on);

        /// <summary>Center pan/tilt.</summary>
        public virtual void Home()
        {
            GoTo(PanRange.Home, TiltRange.Home, null, null);
        }

        /// <summary>Latest known position (may be cached).</summary>
        public abstract PtzState State { get; }

        /// <summary>Fresh position read, used when saving presets. Defaults to the cached state.</summary>
        public virtual PtzState ReadState() { return State.Clone(); }

        /// <summary>Called ~30 times per second from the UI timer.</summary>
        public virtual void Tick(double dt) { }

        /// <summary>Show this camera's video in the host control. Return false if no preview is available.</summary>
        public virtual bool AttachPreview(Control host) { return false; }
        public virtual void DetachPreview() { }

        /// <summary>True when Snapshot() can grab a picture (the camera's video reaches PTZVC).</summary>
        public virtual bool CanSnapshot { get { return false; } }

        /// <summary>The current video frame, at the camera's resolution. Throws when no frame is available.</summary>
        public virtual Bitmap Snapshot() { throw new NotSupportedException("This camera's video does not come through PTZVC."); }

        public virtual void Dispose()
        {
            try { DetachPreview(); } catch { }
            try { Disconnect(); } catch { }
        }
    }

    /// <summary>
    /// Helper for backends that only support absolute positioning (UVC, simulator-driven):
    /// turns press-and-hold into smooth absolute steps on each Tick.
    /// </summary>
    public class Jogger
    {
        public int PanDir, TiltDir, ZoomDir, FocusDir;
        public double PanTiltSpeed = 0.5, ZoomSpeed = 0.5, FocusSpeed = 0.5;

        public bool Active { get { return PanDir != 0 || TiltDir != 0 || ZoomDir != 0 || FocusDir != 0; } }

        /// <summary>Seconds to sweep the full range at speed 1.0 and at speed 0.0.</summary>
        public static double SweepTime(double speed, double fast, double slow)
        {
            speed = Math.Max(0, Math.Min(1, speed));
            // exponential feel: fine control at low speed
            return slow * Math.Pow(fast / slow, speed);
        }
    }
}
