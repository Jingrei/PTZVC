using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PTZVC
{
    public partial class MainForm : IMessageFilter
    {
        public const string VirtualCameraMutex = "PTZVC.VirtualViscaCamera";

        class DeviceItem
        {
            public string Label, Key;
            public UvcDevice Uvc;
            public ViscaEntry Visca;
            public bool IsAdd;

            public PtzCamera Create()
            {
                if (Uvc != null) return new UvcCamera(Uvc);
                if (Visca != null) return Visca.Create();
                return new SimCamera();
            }

            public override string ToString() { return Label; }
        }

        PresetStore presets;
        Settings settings;
        PtzCamera cam;
        DeviceItem currentItem;
        PositionMap posMap;
        Label previewMessage;
        bool suppressCombo;

        readonly Stopwatch clock = Stopwatch.StartNew();
        System.Windows.Forms.Timer timer;
        double lastTick;

        // drive state
        int padPan, padTilt, kbPan, kbTilt, kbZoom, kbFocus;
        int sentPan, sentTilt, sentZoom, sentFocus;
        double sentSpeed = -1;
        bool invertPan, invertTilt;

        // sliders
        double zoomHoldUntil, focusHoldUntil, afHoldUntil, lastZoomSend, lastFocusSend;
        bool zoomPending, focusPending;

        // presets
        bool saveArmed, clearArmed;
        double armedAt;
        int lastRecalled;

        double flashUntil; // snapshot flash

        // status line
        string tempStatus;
        Color tempColor;
        double tempUntil;

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        double Speed { get { return speedSlider.Fraction; } }

        // keys currently held down in this window (tracked from WM_KEYDOWN / WM_KEYUP)
        readonly HashSet<Keys> held = new HashSet<Keys>();
        bool KeyHeld(Keys k) { return held.Contains(k); }

        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101;
            if (m.Msg != WM_KEYDOWN && m.Msg != WM_KEYUP) return false;
            Control c = Control.FromHandle(m.HWnd);
            if (c == null || c.FindForm() != this) return false; // dialogs keep their keys
            Keys k = (Keys)(int)(long)m.WParam & Keys.KeyCode;
            if (m.Msg == WM_KEYDOWN) held.Add(k); else held.Remove(k);
            return false;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            held.Clear(); // never leave the camera moving when focus leaves the window
            base.OnDeactivate(e);
        }

        void Init()
        {
            presets = new PresetStore();
            settings = new Settings();
            speedSlider.Value = (int)Math.Round(settings.GetDouble("speed", 50));
            speedText.Text = speedSlider.Value + "%";

            dpad.DirectionChanged += delegate(int p, int t) { padPan = p; padTilt = t; UpdateDrives(); };
            dpad.CenterClicked += delegate { Center(); };
            foreach (RemoteButton b in new RemoteButton[] { zoomWide, zoomTele, focusFar, focusNear })
            {
                b.Pressed += delegate { UpdateDrives(); };
                b.Released += delegate { UpdateDrives(); };
            }
            speedSlider.ValueChanged += delegate
            {
                speedText.Text = speedSlider.Value + "%";
                remote.Invalidate();
                UpdateDrives();
            };

            zoomSlider.ValueChanged += delegate { zoomHoldUntil = Now + 1.5; zoomPending = true; FlushSliders(false); };
            zoomSlider.DragEnded += delegate { FlushSliders(true); };
            focusSlider.ValueChanged += delegate { focusHoldUntil = Now + 1.5; focusPending = true; FlushSliders(false); };
            focusSlider.DragEnded += delegate { FlushSliders(true); };

            afToggle.CheckedChanged += delegate { ApplyAutoFocus(); };
            saveButton.Pressed += delegate { ArmSave(!saveArmed); };
            clearButton.Pressed += delegate { ArmClear(!clearArmed); };
            for (int i = 1; i <= 12; i++)
            {
                int slot = i;
                presetButtons[i].Pressed += delegate { PresetPressed(slot); };
                presetButtons[i].MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) PresetMenu(slot); };
            }

            cameraCombo.SelectedIndexChanged += delegate { OnComboChanged(); };
            snapButton.Pressed += delegate { TakeSnapshot(); };
            refreshButton.Pressed += delegate { RefreshDevices(currentItem != null ? currentItem.Key : null, true); };
            menuButton.Pressed += delegate { ShowMainMenu(); };
            previewHost.ControlAdded += delegate(object s, ControlEventArgs e) { e.Control.MouseWheel += PreviewWheel; };

            timer = new System.Windows.Forms.Timer { Interval = 20 };
            timer.Tick += delegate { Tick(); };

            Application.AddMessageFilter(this);
            Shown += delegate
            {
                LayoutTopBar();
                RefreshDevices(settings.Get("lastCamera", "virtual"), false);
                timer.Start();
            };
        }

        // ---- devices -------------------------------------------------------------------

        void RefreshDevices(string selectKey, bool forceReconnect)
        {
            suppressCombo = true;
            cameraCombo.Items.Clear();
            List<DeviceItem> items = new List<DeviceItem>();
            items.Add(new DeviceItem { Label = "Virtual PTZ Camera (built-in simulator)", Key = "virtual" });
            foreach (UvcDevice d in UvcDevices.List())
                items.Add(new DeviceItem { Label = "USB: " + d.Name, Key = "uvc:" + d.Path, Uvc = d });
            for (int i = 0; i < settings.ViscaCameras.Count; i++)
            {
                ViscaEntry e = settings.ViscaCameras[i];
                string where = e.Protocol == ViscaProtocol.Serial ? e.Host : e.Host + ":" + e.Port;
                items.Add(new DeviceItem { Label = "VISCA: " + e.Label + "  (" + where + ")", Key = "visca:" + e.Label, Visca = e });
            }
            items.Add(new DeviceItem { Label = "+ Add VISCA / IP camera…", Key = "add", IsAdd = true });

            int sel = 0;
            for (int i = 0; i < items.Count; i++)
            {
                cameraCombo.Items.Add(items[i]);
                if (items[i].Key == selectKey) sel = i;
            }
            cameraCombo.SelectedIndex = sel;
            suppressCombo = false;
            DeviceItem chosen = items[sel];
            if (forceReconnect || currentItem == null || currentItem.Key != chosen.Key) SelectItem(chosen);
        }

        void OnComboChanged()
        {
            if (suppressCombo) return;
            DeviceItem item = cameraCombo.SelectedItem as DeviceItem;
            if (item == null) return;
            if (item.IsAdd)
            {
                RestoreComboSelection();
                BeginInvoke((MethodInvoker)delegate { AddViscaCamera(null); });
                return;
            }
            if (currentItem == null || item.Key != currentItem.Key) SelectItem(item);
            ActiveControl = null; // keep arrow keys for the camera, not the list
        }

        void RestoreComboSelection()
        {
            suppressCombo = true;
            for (int i = 0; i < cameraCombo.Items.Count; i++)
                if (currentItem != null && ((DeviceItem)cameraCombo.Items[i]).Key == currentItem.Key) cameraCombo.SelectedIndex = i;
            suppressCombo = false;
        }

        void SelectItem(DeviceItem item)
        {
            CloseCamera();
            currentItem = item;
            Cursor = Cursors.WaitCursor;
            SetStatus("Connecting to " + item.Label + " …", Theme.TextDim, 30);
            readouts.Refresh();
            PtzCamera c = item.Create();
            try
            {
                c.Connect();
            }
            catch (Exception ex)
            {
                try { c.Dispose(); } catch { }
                Cursor = Cursors.Default;
                ShowPreviewMessage("Could not connect to " + item.Label + "\n\n" + ex.Message);
                SetStatus("Connection failed: " + ex.Message.Split('\n')[0], Theme.Bad, 15);
                remote.LedColor = Theme.Bad; remote.Invalidate();
                ConfigureControls();
                return;
            }
            Cursor = Cursors.Default;
            cam = c;
            invertPan = settings.GetBool("invertPan:" + cam.Id);
            invertTilt = settings.GetBool("invertTilt:" + cam.Id);
            if (!cam.AttachPreview(previewHost))
            {
                posMap = new PositionMap
                {
                    Dock = DockStyle.Fill,
                    Camera = cam,
                    PresetLookup = delegate(int slot) { return cam == null ? null : presets.Get(cam.Id, slot); },
                    Note = cam.Kind + "\nNetwork/serial cameras send video over HDMI/SDI/NDI/RTSP — watch it in your video app or monitor.\nThis map shows where the camera is pointing and where your presets are."
                };
                previewHost.Controls.Add(posMap);
            }
            afToggle.Checked = cam.SupportsAutoFocus ? cam.State.AutoFocus : false;
            sentPan = sentTilt = sentZoom = sentFocus = 0;
            lastRecalled = 0;
            ConfigureControls();
            RefreshPresetButtons();
            settings.Set("lastCamera", item.Key);
            settings.Save();
            remote.LedColor = Theme.Good; remote.Invalidate();
            SetStatus("Connected: " + cam.Name, Theme.Good, 4);
        }

        void CloseCamera()
        {
            ArmSave(false); ArmClear(false);
            if (cam != null)
            {
                try { cam.Dispose(); } catch { }
                cam = null;
            }
            foreach (Control ctl in new List<Control>(EnumerateChildren(previewHost))) { previewHost.Controls.Remove(ctl); ctl.Dispose(); }
            posMap = null;
            previewMessage = null;
            remote.LedColor = Theme.TextFaint; remote.Invalidate();
        }

        static IEnumerable<Control> EnumerateChildren(Control c)
        {
            foreach (Control x in c.Controls) yield return x;
        }

        void ShowPreviewMessage(string text)
        {
            previewMessage = new Label
            {
                Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.TextDim, BackColor = Color.Black, Font = Theme.Normal
            };
            previewHost.Controls.Add(previewMessage);
        }

        void ConfigureControls()
        {
            bool on = cam != null;
            dpad.Enabled = on && (cam.PanRange.Supported || cam.TiltRange.Supported);
            bool zoom = on && cam.ZoomRange.Supported;
            zoomWide.Enabled = zoomTele.Enabled = zoomSlider.Enabled = zoom;
            afToggle.Enabled = on && cam.SupportsAutoFocus;
            saveButton.Enabled = clearButton.Enabled = on;
            for (int i = 1; i <= 12; i++) presetButtons[i].Enabled = on;
            snapButton.Enabled = on && cam.CanSnapshot;
            if (on)
            {
                ApplyRanges();
                zoomSlider.Value = cam.State.Zoom;
                focusSlider.Value = FocusToSlider(cam.State.Focus);
            }
            UpdateAfUi();
        }

        void ApplyRanges()
        {
            if (zoomSlider.Minimum != cam.ZoomRange.Min || zoomSlider.Maximum != cam.ZoomRange.Max)
            {
                zoomSlider.Minimum = Math.Min(cam.ZoomRange.Min, zoomSlider.Maximum);
                zoomSlider.Maximum = cam.ZoomRange.Max;
                zoomSlider.Minimum = cam.ZoomRange.Min;
            }
            if (focusSlider.Minimum != cam.FocusRange.Min || focusSlider.Maximum != cam.FocusRange.Max)
            {
                focusSlider.Minimum = Math.Min(cam.FocusRange.Min, focusSlider.Maximum);
                focusSlider.Maximum = cam.FocusRange.Max;
                focusSlider.Minimum = cam.FocusRange.Min;
            }
        }

        // Slider runs FAR (left) -> NEAR (right) regardless of the camera's focus units.
        int FocusToSlider(int focus)
        {
            return cam == null || cam.FocusIncreasesNear ? focus : cam.FocusRange.Min + cam.FocusRange.Max - focus;
        }

        int SliderToFocus(int v) { return FocusToSlider(v); }

        // ---- driving -------------------------------------------------------------------

        void Try(Action a)
        {
            try { a(); }
            catch (Exception ex) { SetStatus("Camera error: " + ex.Message, Theme.Bad, 6); }
        }

        static int Clamp1(int v) { return v < -1 ? -1 : (v > 1 ? 1 : v); }

        void UpdateDrives()
        {
            if (cam == null) return;
            double spd = Speed;
            bool speedChanged = Math.Abs(spd - sentSpeed) > 0.001;

            int p = Clamp1(padPan + kbPan), t = Clamp1(padTilt + kbTilt);
            if (invertPan) p = -p;
            if (invertTilt) t = -t;
            if (p != sentPan || t != sentTilt || ((p != 0 || t != 0) && speedChanged))
            {
                sentPan = p; sentTilt = t;
                Try(delegate { cam.DrivePanTilt(p, t, spd); });
            }

            int z = (zoomTele.IsDown || kbZoom > 0) ? 1 : (zoomWide.IsDown || kbZoom < 0) ? -1 : 0;
            if (!cam.ZoomRange.Supported) z = 0;
            if (z != sentZoom || (z != 0 && speedChanged))
            {
                sentZoom = z;
                Try(delegate { cam.DriveZoom(z, spd); });
                if (z != 0) zoomHoldUntil = 0;
            }

            int f = (focusNear.IsDown || kbFocus > 0) ? 1 : (focusFar.IsDown || kbFocus < 0) ? -1 : 0;
            if (afToggle.Checked || !cam.FocusRange.Supported) f = 0;
            if (f != sentFocus || (f != 0 && speedChanged))
            {
                sentFocus = f;
                Try(delegate { cam.DriveFocus(f, spd); });
                if (f != 0) focusHoldUntil = 0;
            }
            sentSpeed = spd;
        }

        void Center()
        {
            if (cam == null) return;
            Try(delegate { cam.Home(); });
            SetStatus("Centering camera (home position)", Theme.TextDim, 3);
        }

        void FlushSliders(bool force)
        {
            if (cam == null) return;
            double now = Now;
            if (zoomPending && (force || now - lastZoomSend > 0.08))
            {
                zoomPending = false; lastZoomSend = now;
                int v = zoomSlider.Value;
                Try(delegate { cam.GoTo(null, null, v, null); });
            }
            if (focusPending && (force || now - lastFocusSend > 0.08))
            {
                focusPending = false; lastFocusSend = now;
                if (!afToggle.Checked)
                {
                    int v = SliderToFocus(focusSlider.Value);
                    Try(delegate { cam.GoTo(null, null, null, v); });
                }
            }
        }

        void PreviewWheel(object sender, MouseEventArgs e)
        {
            if (cam == null || !cam.ZoomRange.Supported) return;
            int step = (int)(cam.ZoomRange.Span * 0.04) * Math.Sign(e.Delta);
            int target = cam.ZoomRange.Clamp(Math.Max(zoomSlider.Value, cam.ZoomRange.Min) + step);
            zoomSlider.Value = target;
            zoomHoldUntil = Now + 1.5;
            Try(delegate { cam.GoTo(null, null, target, null); });
        }

        void ApplyAutoFocus()
        {
            afHoldUntil = Now + 2;
            UpdateAfUi();
            if (cam == null) return;
            bool on = afToggle.Checked;
            Try(delegate { cam.SetAutoFocus(on); });
            if (on) { sentFocus = 0; }
            SetStatus(on ? "Auto focus ON — presets will not change focus" : "Auto focus OFF — use FAR/NEAR or the slider; presets restore saved focus", Theme.TextDim, 4);
        }

        void UpdateAfUi()
        {
            bool manualOk = cam != null && cam.FocusRange.Supported && !afToggle.Checked;
            focusFar.Enabled = focusNear.Enabled = focusSlider.Enabled = manualOk;
            afText.Text = cam == null || !cam.SupportsAutoFocus ? "N/A" : (afToggle.Checked ? "ON" : "OFF");
            remote.Invalidate();
        }

        // ---- presets ------------------------------------------------------------------

        void ArmSave(bool on)
        {
            if (on && cam == null) { SetStatus("Select a camera first", Theme.Bad, 3); return; }
            if (on) clearArmed = false;
            saveArmed = on;
            armedAt = Now;
            if (on) SetStatus("SAVE PRESET: click a number (1–12) to store the current pan, tilt, zoom and focus.  Esc cancels.", Theme.Armed, 12);
            RefreshPresetButtons();
        }

        void ArmClear(bool on)
        {
            if (on && cam == null) return;
            if (on) saveArmed = false;
            clearArmed = on;
            armedAt = Now;
            if (on) SetStatus("CLEAR: click the preset number to erase.  Esc cancels.", Theme.Bad, 12);
            RefreshPresetButtons();
        }

        void PresetPressed(int slot)
        {
            if (cam == null) { SetStatus("Select a camera first", Theme.Bad, 3); return; }
            if (saveArmed) { SavePreset(slot); return; }
            if (clearArmed)
            {
                presets.Clear(cam.Id, slot);
                if (lastRecalled == slot) lastRecalled = 0;
                ArmClear(false);
                SetStatus("Preset " + slot + " cleared", Theme.TextDim, 4);
                return;
            }
            RecallPreset(slot);
        }

        void SavePreset(int slot)
        {
            PtzState s = null;
            Try(delegate { s = cam.ReadState(); });
            if (s == null) return;
            presets.Set(cam.Id, slot, new Preset { Pan = s.Pan, Tilt = s.Tilt, Zoom = s.Zoom, Focus = s.Focus, Saved = DateTime.Now });
            saveArmed = false;
            lastRecalled = slot;
            RefreshPresetButtons();
            SetStatus("Preset " + slot + " saved — " + Describe(s.Pan, s.Tilt, s.Zoom, s.Focus, true), Theme.Good, 6);
        }

        void RecallPreset(int slot)
        {
            Preset p = presets.Get(cam.Id, slot);
            if (p == null)
            {
                SetStatus("Preset " + slot + " is empty — press SAVE PRESET, then " + slot + " to store the current position.", Theme.TextDim, 5);
                return;
            }
            bool applyFocus = !afToggle.Checked && cam.FocusRange.Supported;
            Try(delegate { cam.GoTo(p.Pan, p.Tilt, p.Zoom, applyFocus ? p.Focus : (int?)null); });
            lastRecalled = slot;
            zoomHoldUntil = 0;
            focusHoldUntil = 0;
            RefreshPresetButtons();
            SetStatus("Preset " + slot + " — " + Describe(p.Pan, p.Tilt, p.Zoom, p.Focus, applyFocus) +
                (applyFocus ? "" : "   (auto focus on: saved focus not applied)"), Theme.Text, 5);
        }

        string Describe(int pan, int tilt, int zoom, int focus, bool withFocus)
        {
            string s = "pan " + cam.Format(Axis.Pan, pan) + ", tilt " + cam.Format(Axis.Tilt, tilt) + ", zoom " + cam.Format(Axis.Zoom, zoom);
            if (withFocus) s += ", focus " + cam.Format(Axis.Focus, focus);
            return s;
        }

        void RefreshPresetButtons()
        {
            int saved = 0;
            for (int i = 1; i <= 12; i++)
            {
                RemoteButton b = presetButtons[i];
                bool has = cam != null && presets.Get(cam.Id, i) != null;
                if (has) saved++;
                b.Dot = has;
                if (saveArmed) { b.Lit = true; b.LitColor = Theme.Armed; b.ForeColor = Theme.Armed; }
                else if (clearArmed) { b.Lit = has; b.LitColor = Theme.Bad; b.ForeColor = has ? Theme.Bad : Theme.TextFaint; }
                else { b.Lit = i == lastRecalled && has; b.LitColor = Theme.Lit; b.ForeColor = Theme.Text; }
                b.Invalidate();
            }
            saveButton.Lit = saveArmed;
            saveButton.Text = saveArmed ? "PICK A NUMBER…" : "SAVE PRESET";
            saveButton.ForeColor = saveArmed ? Theme.Armed : Theme.Text;
            clearButton.Lit = clearArmed;
            clearButton.ForeColor = clearArmed ? Theme.Bad : Theme.Text;
            presetHint.Text = saveArmed ? "SAVE → PICK A NUMBER" : clearArmed ? "CLEAR → PICK A NUMBER" : (cam == null ? "" : saved + " OF 12 SAVED");
            presetHint.Color = saveArmed ? Theme.Armed : clearArmed ? Theme.Bad : Theme.TextFaint;
            remote.Invalidate();
            if (posMap != null) posMap.Invalidate();
        }

        void PresetMenu(int slot)
        {
            if (cam == null) return;
            DarkMenu m = new DarkMenu();
            m.Add("Save current position to preset " + slot, delegate { SavePreset(slot); });
            ToolStripMenuItem clr = m.Add("Clear preset " + slot, delegate
            {
                presets.Clear(cam.Id, slot);
                if (lastRecalled == slot) lastRecalled = 0;
                RefreshPresetButtons();
                SetStatus("Preset " + slot + " cleared", Theme.TextDim, 4);
            });
            clr.Enabled = presets.Get(cam.Id, slot) != null;
            m.Show(presetButtons[slot], new Point(0, presetButtons[slot].Height));
        }

        // ---- snapshot -----------------------------------------------------------------------

        public static string SnapshotDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "PTZVC"); } }

        void TakeSnapshot()
        {
            if (cam == null) { SetStatus("Select a camera first", Theme.Bad, 3); return; }
            if (!cam.CanSnapshot)
            {
                SetStatus(currentItem != null && currentItem.Uvc != null
                    ? "No snapshot: no live video from this camera. Close other apps using it, then press ⟳."
                    : "No snapshot: this camera sends its video over HDMI/SDI/NDI/RTSP, not through PTZVC.", Theme.Bad, 6);
                return;
            }
            try
            {
                string file;
                using (Bitmap shot = cam.Snapshot())
                {
                    Directory.CreateDirectory(SnapshotDir);
                    file = SnapshotFile(SnapshotDir, cam.Name, DateTime.Now);
                    shot.Save(file, ImageFormat.Png);
                }
                previewFrame.BackColor = Theme.Lit; // camera "flash" around the picture
                flashUntil = Now + 0.15;
                SetStatus("Snapshot saved: " + file, Theme.Good, 6);
            }
            catch (Exception ex) { SetStatus("Snapshot failed: " + ex.Message, Theme.Bad, 6); }
        }

        /// <summary>"Camera name 2026-10-17 13-05-09.png", numbered if that name is taken.</summary>
        public static string SnapshotFile(string dir, string cameraName, DateTime when)
        {
            string name = cameraName;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string stem = Path.Combine(dir, name.Trim() + " " + when.ToString("yyyy-MM-dd HH-mm-ss"));
            string file = stem + ".png";
            for (int i = 2; File.Exists(file); i++) file = stem + " (" + i + ").png";
            return file;
        }

        static void OpenFolder(string dir)
        {
            Directory.CreateDirectory(dir);
            Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        // ---- per-frame update ---------------------------------------------------------------

        void Tick()
        {
            double now = Now;
            double dt = now - lastTick;
            lastTick = now;
            PollKeyboard();
            FlushSliders(false);
            if (flashUntil > 0 && now > flashUntil) { flashUntil = 0; previewFrame.BackColor = Theme.Window; }
            if ((saveArmed || clearArmed) && now - armedAt > 15)
            {
                ArmSave(false); ArmClear(false);
                SetStatus("Preset selection timed out", Theme.TextDim, 3);
            }

            if (cam != null)
            {
                try { cam.Tick(dt); }
                catch (Exception ex) { SetStatus("Camera error: " + ex.Message, Theme.Bad, 5); }
                SyncFromCamera(now);
            }
            else
            {
                for (int i = 0; i < 5; i++) readouts.Set(i, "--", -1);
            }

            // status line
            if (tempStatus != null && now < tempUntil) readouts.SetStatus(tempStatus, tempColor);
            else
            {
                tempStatus = null;
                if (cam == null) readouts.SetStatus(currentItem == null ? "No camera selected" : "Not connected — pick a camera, or press ⟳ to retry", Theme.TextDim);
                else if (cam.Problem != null) readouts.SetStatus("⚠ " + cam.Problem, Theme.Bad);
                else readouts.SetStatus("Connected · " + cam.Name + " · " + cam.Kind + "     •  Arrows pan/tilt, PgUp/PgDn zoom, S then 1–12 saves a preset  (⋯ menu → Keyboard shortcuts)", Theme.TextDim);
            }

            // LED: blink amber while waiting for a preset number
            Color led = cam == null ? (currentItem != null && previewMessage != null ? Theme.Bad : Theme.TextFaint)
                : (saveArmed || clearArmed) ? ((now * 2.5) % 1 < 0.5 ? (saveArmed ? Theme.Armed : Theme.Bad) : Theme.TextFaint)
                : cam.Problem != null ? Theme.Bad : Theme.Good;
            if (led != remote.LedColor) { remote.LedColor = led; remote.Invalidate(); }
        }

        void SyncFromCamera(double now)
        {
            PtzState s = cam.State;
            ApplyRanges();
            readouts.Set(0, cam.PanRange.Supported ? cam.Format(Axis.Pan, s.Pan) : "n/a", cam.PanRange.Supported ? (s.Pan - cam.PanRange.Min) / cam.PanRange.Span : -1);
            readouts.Set(1, cam.TiltRange.Supported ? cam.Format(Axis.Tilt, s.Tilt) : "n/a", cam.TiltRange.Supported ? (s.Tilt - cam.TiltRange.Min) / cam.TiltRange.Span : -1);
            readouts.Set(2, cam.ZoomRange.Supported ? cam.Format(Axis.Zoom, s.Zoom) : "n/a", cam.ZoomRange.Supported ? (s.Zoom - cam.ZoomRange.Min) / cam.ZoomRange.Span : -1);
            readouts.Set(3, cam.FocusRange.Supported ? cam.Format(Axis.Focus, s.Focus) : "n/a",
                cam.FocusRange.Supported ? (FocusToSlider(s.Focus) - cam.FocusRange.Min) / cam.FocusRange.Span : -1);
            readouts.Set(4, !cam.SupportsAutoFocus ? "n/a" : (afToggle.Checked ? "AUTO" : "MANUAL"), -1);

            if (!zoomSlider.IsDragging && now > zoomHoldUntil) zoomSlider.Value = s.Zoom;
            if (!focusSlider.IsDragging && now > focusHoldUntil) focusSlider.Value = FocusToSlider(s.Focus);

            // follow the camera if its focus mode changed elsewhere (e.g. its own remote)
            if (cam.SupportsAutoFocus && now > afHoldUntil && s.AutoFocus != afToggle.Checked)
            {
                afToggle.Checked = s.AutoFocus;
                UpdateAfUi();
            }
            if (posMap != null) posMap.Invalidate();
        }

        // ---- keyboard ---------------------------------------------------------------------

        void PollKeyboard()
        {
            int p = 0, t = 0, z = 0, f = 0;
            if (!cameraCombo.DroppedDown)
            {
                if (KeyHeld(Keys.Left)) p--;
                if (KeyHeld(Keys.Right)) p++;
                if (KeyHeld(Keys.Up)) t++;
                if (KeyHeld(Keys.Down)) t--;
                if (KeyHeld(Keys.PageUp) || KeyHeld(Keys.Add)) z++;
                if (KeyHeld(Keys.PageDown) || KeyHeld(Keys.Subtract)) z--;
                if (KeyHeld(Keys.OemCloseBrackets)) f++;
                if (KeyHeld(Keys.OemOpenBrackets)) f--;
            }
            if (p == kbPan && t == kbTilt && z == kbZoom && f == kbFocus) return;
            kbPan = p; kbTilt = t; kbZoom = z; kbFocus = f;
            dpad.ShowKeyDirection(p, t);
            zoomTele.SetVisualDown(z > 0); zoomWide.SetVisualDown(z < 0);
            focusNear.SetVisualDown(f > 0 && !afToggle.Checked); focusFar.SetVisualDown(f < 0 && !afToggle.Checked);
            UpdateDrives();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100;
            if (cameraCombo.DroppedDown || msg.Msg != WM_KEYDOWN) return base.ProcessCmdKey(ref msg, keyData);
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) != 0) return base.ProcessCmdKey(ref msg, keyData);

            switch (k)
            {
                // held keys are handled by PollKeyboard; swallow them so they don't move focus
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Add: case Keys.Subtract:
                case Keys.OemOpenBrackets: case Keys.OemCloseBrackets:
                    return true;
            }

            bool repeat = ((long)msg.LParam & (1L << 30)) != 0;
            if (repeat) return base.ProcessCmdKey(ref msg, keyData);

            int slot = 0;
            if (k >= Keys.D1 && k <= Keys.D9) slot = k - Keys.D0;
            else if (k >= Keys.NumPad1 && k <= Keys.NumPad9) slot = k - Keys.NumPad0;
            else if (k == Keys.D0 || k == Keys.NumPad0) slot = 10;
            else if (k == Keys.OemMinus) slot = 11;
            else if (k == Keys.Oemplus) slot = 12;
            if (slot > 0) { PresetPressed(slot); return true; }

            switch (k)
            {
                case Keys.S: ArmSave(!saveArmed); return true;
                case Keys.Delete: ArmClear(!clearArmed); return true;
                case Keys.H: case Keys.Home: Center(); return true;
                case Keys.F: if (afToggle.Enabled) afToggle.Toggle(); return true;
                case Keys.P: TakeSnapshot(); return true;
                case Keys.Escape:
                    if (saveArmed || clearArmed) { ArmSave(false); ArmClear(false); SetStatus("Cancelled", Theme.TextDim, 2); }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- status --------------------------------------------------------------------------

        void SetStatus(string text, Color color, double seconds)
        {
            tempStatus = text; tempColor = color; tempUntil = Now + seconds;
            readouts.SetStatus(text, color);
        }

        // ---- menu / VISCA management ----------------------------------------------------------

        void ShowMainMenu()
        {
            mainMenu.Items.Clear();
            mainMenu.Add("Add VISCA camera (IP or serial)…", delegate { AddViscaCamera(null); });
            mainMenu.Add("Launch virtual VISCA camera (test network control)", delegate { LaunchVirtualVisca(); });
            if (currentItem != null && currentItem.Visca != null)
            {
                ViscaEntry v = currentItem.Visca;
                mainMenu.Add("Edit “" + v.Label + "”…", delegate { AddViscaCamera(v); });
                mainMenu.Add("Remove “" + v.Label + "”", delegate
                {
                    settings.ViscaCameras.Remove(v);
                    settings.Save();
                    RefreshDevices("virtual", false);
                });
            }
            mainMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem ip = mainMenu.Add("Invert pan direction", delegate { SetInvert(true, !invertPan); });
            ip.Checked = invertPan; ip.Enabled = cam != null;
            ToolStripMenuItem it = mainMenu.Add("Invert tilt direction (ceiling mount)", delegate { SetInvert(false, !invertTilt); });
            it.Checked = invertTilt; it.Enabled = cam != null;
            mainMenu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem snap = mainMenu.Add("Take snapshot (P)", delegate { TakeSnapshot(); });
            snap.Enabled = cam != null && cam.CanSnapshot;
            mainMenu.Add("Open snapshots folder", delegate { OpenFolder(SnapshotDir); });
            mainMenu.Items.Add(new ToolStripSeparator());
            mainMenu.Add("Reconnect camera", delegate { RefreshDevices(currentItem != null ? currentItem.Key : "virtual", true); });
            mainMenu.Add("Open presets folder", delegate { OpenFolder(AppPaths.Dir); });
            mainMenu.Add("Keyboard shortcuts", delegate { DarkMessage.Show(this, "Keyboard shortcuts", Shortcuts); });
            mainMenu.Add("About PTZVC", delegate { DarkMessage.Show(this, "About PTZVC", About, Brand.Vertical); });
            mainMenu.Show(menuButton, new Point(menuButton.Width - mainMenu.PreferredSize.Width, menuButton.Height + 2));
        }

        void SetInvert(bool pan, bool value)
        {
            if (cam == null) return;
            if (pan) invertPan = value; else invertTilt = value;
            settings.Set((pan ? "invertPan:" : "invertTilt:") + cam.Id, value);
            settings.Save();
        }

        void AddViscaCamera(ViscaEntry existing)
        {
            using (AddViscaDialog d = new AddViscaDialog(existing))
            {
                d.LaunchVirtual = delegate { LaunchVirtualProcess(); };
                if (d.ShowDialog(this) != DialogResult.OK) return;
                ViscaEntry e = d.Result;
                if (existing != null) settings.ViscaCameras.Remove(existing);
                settings.ViscaCameras.RemoveAll(x => x.Label == e.Label);
                settings.ViscaCameras.Add(e);
                settings.Save();
                RefreshDevices("visca:" + e.Label, true);
            }
        }

        public static bool VirtualCameraRunning()
        {
            Mutex m;
            if (Mutex.TryOpenExisting(VirtualCameraMutex, out m)) { m.Dispose(); return true; }
            return false;
        }

        static void LaunchVirtualProcess()
        {
            if (VirtualCameraRunning()) return;
            Process.Start(Application.ExecutablePath, "--virtual-camera");
        }

        void LaunchVirtualVisca()
        {
            bool already = VirtualCameraRunning();
            LaunchVirtualProcess();
            const string label = "Virtual VISCA Camera";
            if (!settings.ViscaCameras.Exists(x => x.Label == label))
            {
                settings.ViscaCameras.Add(new ViscaEntry { Label = label, Protocol = ViscaProtocol.SonyUdp, Host = "127.0.0.1", Port = Visca.SonyUdpPort, Address = 1 });
                settings.Save();
            }
            SetStatus(already ? "Virtual VISCA camera already running — connecting…" : "Starting virtual VISCA camera…", Theme.TextDim, 5);
            System.Windows.Forms.Timer delay = new System.Windows.Forms.Timer { Interval = already ? 50 : 1500 };
            delay.Tick += delegate
            {
                delay.Stop(); delay.Dispose();
                RefreshDevices("visca:" + label, true);
            };
            delay.Start();
        }

        const string Shortcuts =
            "Arrow keys\tPan / tilt (hold; combine for diagonals)\n" +
            "H or Home\tCenter (home position)\n" +
            "Page Up / Down\tZoom in / out (also numpad + / -)\n" +
            "[  and  ]\tFocus far / near (auto focus off)\n" +
            "F\tToggle auto focus\n" +
            "S\tSave preset (then press the number)\n" +
            "Delete\tClear preset (then press the number)\n" +
            "1 – 9, 0, -, =\tRecall presets 1 – 12\n" +
            "P\tSnapshot (saved to Pictures\\PTZVC)\n" +
            "Esc\tCancel save / clear\n" +
            "Mouse wheel\tZoom (over the video)\n" +
            "Right-click\ta preset button for save / clear";

        const string About =
            "PTZVC — PTZ Virtual Controller\n\n" +
            "Controls PTZ cameras over:\n" +
            "  • USB / UVC (DirectShow camera control, with live video)\n" +
            "  • VISCA over IP (Sony UDP 52381), VISCA TCP / UDP (raw)\n" +
            "  • VISCA serial (RS-232 / RS-485)\n\n" +
            "No camera? Use the built-in Virtual PTZ Camera, or launch the\n" +
            "virtual VISCA camera from the ⋯ menu to test real network control.\n\n" +
            "Presets are saved per camera in %APPDATA%\\PTZVC.\n" +
            "Snapshots (P) are saved in Pictures\\PTZVC.";

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            timer.Stop();
            settings.Set("speed", (double)speedSlider.Value);
            settings.Save();
            CloseCamera();
            base.OnFormClosing(e);
        }
    }
}
