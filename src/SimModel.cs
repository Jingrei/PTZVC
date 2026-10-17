using System;

namespace PTZVC
{
    /// <summary>
    /// Physics of the virtual PTZ head. Units mirror common VISCA cameras so the VISCA
    /// virtual camera can speak them directly:
    ///   pan  -2448..2448  = -170..+170 deg
    ///   tilt  -432..1296  =  -30..+90  deg
    ///   zoom     0..16384 =    1x..20x
    ///   focus 4096..49152 = infinity..0.4 m
    /// Thread-safe: the VISCA server thread and the UI thread both touch it.
    /// </summary>
    public class SimModel
    {
        public const int PanMin = -2448, PanMax = 2448;
        public const int TiltMin = -432, TiltMax = 1296;
        public const int ZoomMin = 0, ZoomMax = 16384;
        public const int FocusMin = 4096, FocusMax = 49152;
        public const double DegPerUnit = 170.0 / 2448.0;
        public const double MaxZoomFactor = 20.0;
        public const double WideHfov = 62.0;
        public const double NearestFocusM = 0.4;

        readonly object sync = new object();

        // current positions (double for smooth motion)
        double pan, tilt, zoom = 0, focus = FocusMin;
        // continuous drive (units per second, signed)
        double panVel, tiltVel, zoomVel, focusVel;
        // absolute targets (NaN = none) and their speeds (units per second)
        double panTarget = double.NaN, tiltTarget = double.NaN, zoomTarget = double.NaN, focusTarget = double.NaN;
        double panTargetSpeed, tiltTargetSpeed;
        bool autoFocus = true;

        /// <summary>Returns the subject distance (m) under the crosshair; set by the scene.</summary>
        public Func<double, double, double> DepthProbe;

        public static double PanDeg(double units) { return units * DegPerUnit; }
        public static double TiltDeg(double units) { return units * DegPerUnit; }
        public static double ZoomFactor(double zoomUnits)
        {
            return Math.Pow(MaxZoomFactor, Math.Max(0, Math.Min(1, zoomUnits / ZoomMax)));
        }
        public static double Hfov(double zoomUnits) { return WideHfov / ZoomFactor(zoomUnits); }

        /// <summary>Normalized focus 0 (infinity) .. 1 (nearest).</summary>
        public static double FocusNorm(double focusUnits)
        {
            return (focusUnits - FocusMin) / (double)(FocusMax - FocusMin);
        }

        public static double FocusUnitsForDistance(double meters)
        {
            double n = Math.Min(1, NearestFocusM / Math.Max(NearestFocusM, meters));
            return FocusMin + n * (FocusMax - FocusMin);
        }

        // ---- commands --------------------------------------------------------

        /// <summary>dir -1/0/+1, speed in degrees per second.</summary>
        public void DrivePanTilt(int panDir, int tiltDir, double panDegPerSec, double tiltDegPerSec)
        {
            lock (sync)
            {
                panTarget = tiltTarget = double.NaN;
                panVel = panDir * panDegPerSec / DegPerUnit;
                tiltVel = tiltDir * tiltDegPerSec / DegPerUnit;
            }
        }

        /// <summary>dir +1 tele / -1 wide, rate = fraction of full range per second.</summary>
        public void DriveZoom(int dir, double rangePerSec)
        {
            lock (sync)
            {
                zoomTarget = double.NaN;
                zoomVel = dir * rangePerSec * (ZoomMax - ZoomMin);
            }
        }

        /// <summary>dir +1 near / -1 far. Ignored in auto focus.</summary>
        public void DriveFocus(int dir, double rangePerSec)
        {
            lock (sync)
            {
                focusTarget = double.NaN;
                focusVel = dir * rangePerSec * (FocusMax - FocusMin);
            }
        }

        public void SetPanTilt(int panUnits, int tiltUnits, double panDegPerSec, double tiltDegPerSec)
        {
            lock (sync)
            {
                panVel = tiltVel = 0;
                panTarget = Clamp(panUnits, PanMin, PanMax);
                tiltTarget = Clamp(tiltUnits, TiltMin, TiltMax);
                panTargetSpeed = Math.Max(1, panDegPerSec / DegPerUnit);
                tiltTargetSpeed = Math.Max(1, tiltDegPerSec / DegPerUnit);
            }
        }

        public void SetZoom(int units)
        {
            lock (sync) { zoomVel = 0; zoomTarget = Clamp(units, ZoomMin, ZoomMax); }
        }

        public void SetFocus(int units)
        {
            lock (sync) { focusVel = 0; focusTarget = Clamp(units, FocusMin, FocusMax); }
        }

        public void SetAutoFocus(bool on)
        {
            lock (sync) { autoFocus = on; focusVel = 0; focusTarget = double.NaN; }
        }

        public void Home()
        {
            SetPanTilt(0, 0, 90, 70);
        }

        public bool AutoFocus { get { lock (sync) return autoFocus; } }

        public PtzState Snapshot()
        {
            lock (sync)
            {
                PtzState s = new PtzState();
                s.Pan = (int)Math.Round(pan);
                s.Tilt = (int)Math.Round(tilt);
                s.Zoom = (int)Math.Round(zoom);
                s.Focus = (int)Math.Round(focus);
                s.AutoFocus = autoFocus;
                return s;
            }
        }

        /// <summary>Exact (non-rounded) values for rendering.</summary>
        public void Read(out double p, out double t, out double z, out double f)
        {
            lock (sync) { p = pan; t = tilt; z = zoom; f = focus; }
        }

        public bool IsMoving
        {
            get
            {
                lock (sync)
                {
                    return panVel != 0 || tiltVel != 0 || zoomVel != 0 || focusVel != 0 ||
                        !double.IsNaN(panTarget) || !double.IsNaN(tiltTarget) ||
                        !double.IsNaN(zoomTarget) || !double.IsNaN(focusTarget);
                }
            }
        }

        // ---- simulation ------------------------------------------------------

        public void Step(double dt)
        {
            if (dt <= 0) return;
            if (dt > 0.25) dt = 0.25;
            lock (sync)
            {
                pan = Clamp(pan + panVel * dt, PanMin, PanMax);
                tilt = Clamp(tilt + tiltVel * dt, TiltMin, TiltMax);
                zoom = Clamp(zoom + zoomVel * dt, ZoomMin, ZoomMax);

                // preset / absolute moves glide at a fixed speed
                if (!double.IsNaN(panTarget)) pan = Approach(pan, panTarget, panTargetSpeed * dt, ref panTarget);
                if (!double.IsNaN(tiltTarget)) tilt = Approach(tilt, tiltTarget, tiltTargetSpeed * dt, ref tiltTarget);
                if (!double.IsNaN(zoomTarget)) zoom = Approach(zoom, zoomTarget, 0.8 * ZoomMax * dt, ref zoomTarget);

                if (autoFocus)
                {
                    // AF hunts toward the distance of whatever is under the crosshair
                    double ideal = FocusMin;
                    if (DepthProbe != null) ideal = FocusUnitsForDistance(DepthProbe(PanDeg(pan), TiltDeg(tilt)));
                    double diff = ideal - focus;
                    focus += diff * Math.Min(1, dt * 6);
                    if (Math.Abs(diff) < 2) focus = ideal;
                }
                else
                {
                    focus = Clamp(focus + focusVel * dt, FocusMin, FocusMax);
                    if (!double.IsNaN(focusTarget)) focus = Approach(focus, focusTarget, 0.8 * (FocusMax - FocusMin) * dt, ref focusTarget);
                }
            }
        }

        static double Approach(double cur, double target, double maxStep, ref double targetField)
        {
            double d = target - cur;
            if (Math.Abs(d) <= maxStep)
            {
                targetField = double.NaN;
                return target;
            }
            return cur + Math.Sign(d) * maxStep;
        }

        static double Clamp(double v, double lo, double hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
