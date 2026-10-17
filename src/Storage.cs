using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PTZVC
{
    public class Preset
    {
        public int Pan, Tilt, Zoom, Focus;
        public DateTime Saved;
    }

    public static class AppPaths
    {
        public static string Dir
        {
            get
            {
                string d = Environment.GetEnvironmentVariable("PTZVC_DATA");
                if (string.IsNullOrEmpty(d))
                    d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PTZVC");
                Directory.CreateDirectory(d);
                return d;
            }
        }
    }

    /// <summary>Presets per camera, stored as tab-separated text in %APPDATA%\PTZVC\presets.txt.</summary>
    public class PresetStore
    {
        readonly string file;
        readonly Dictionary<string, Dictionary<int, Preset>> data = new Dictionary<string, Dictionary<int, Preset>>();

        public PresetStore() : this(Path.Combine(AppPaths.Dir, "presets.txt")) { }

        public PresetStore(string file)
        {
            this.file = file;
            Load();
        }

        Dictionary<int, Preset> For(string cameraId)
        {
            Dictionary<int, Preset> d;
            if (!data.TryGetValue(cameraId, out d)) { d = new Dictionary<int, Preset>(); data[cameraId] = d; }
            return d;
        }

        public Preset Get(string cameraId, int slot)
        {
            Preset p;
            return For(cameraId).TryGetValue(slot, out p) ? p : null;
        }

        public void Set(string cameraId, int slot, Preset p)
        {
            For(cameraId)[slot] = p;
            Save();
        }

        public void Clear(string cameraId, int slot)
        {
            if (For(cameraId).Remove(slot)) Save();
        }

        void Load()
        {
            if (!File.Exists(file)) return;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                string[] f = line.Split('\t');
                if (f.Length < 7 || line.StartsWith("#")) continue;
                try
                {
                    Preset p = new Preset
                    {
                        Pan = int.Parse(f[2], CultureInfo.InvariantCulture),
                        Tilt = int.Parse(f[3], CultureInfo.InvariantCulture),
                        Zoom = int.Parse(f[4], CultureInfo.InvariantCulture),
                        Focus = int.Parse(f[5], CultureInfo.InvariantCulture),
                        Saved = DateTime.Parse(f[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    };
                    For(f[0])[int.Parse(f[1], CultureInfo.InvariantCulture)] = p;
                }
                catch { }
            }
        }

        void Save()
        {
            StringBuilder sb = new StringBuilder("# PTZVC presets: camera, slot, pan, tilt, zoom, focus, saved\n");
            foreach (KeyValuePair<string, Dictionary<int, Preset>> cam in data)
                foreach (KeyValuePair<int, Preset> kv in cam.Value)
                    sb.Append(cam.Key).Append('\t').Append(kv.Key).Append('\t')
                      .Append(kv.Value.Pan).Append('\t').Append(kv.Value.Tilt).Append('\t')
                      .Append(kv.Value.Zoom).Append('\t').Append(kv.Value.Focus).Append('\t')
                      .Append(kv.Value.Saved.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
            try { File.WriteAllText(file, sb.ToString(), Encoding.UTF8); } catch { }
        }
    }

    /// <summary>A VISCA camera the user added.</summary>
    public class ViscaEntry
    {
        public string Label;
        public ViscaProtocol Protocol;
        public string Host;
        public int Port;
        public int Address = 1;

        public ViscaCamera Create() { return new ViscaCamera(Label, Protocol, Host, Port, Address); }
    }

    /// <summary>Simple key=value settings plus the saved VISCA camera list.</summary>
    public class Settings
    {
        readonly string file = Path.Combine(AppPaths.Dir, "settings.txt");
        readonly string camFile = Path.Combine(AppPaths.Dir, "cameras.txt");
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        public readonly List<ViscaEntry> ViscaCameras = new List<ViscaEntry>();

        public Settings()
        {
            try
            {
                if (File.Exists(file))
                    foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) values[line.Substring(0, i)] = line.Substring(i + 1);
                    }
                if (File.Exists(camFile))
                    foreach (string line in File.ReadAllLines(camFile, Encoding.UTF8))
                    {
                        string[] f = line.Split('\t');
                        if (f.Length < 5) continue;
                        ViscaEntry e = new ViscaEntry { Label = f[0], Host = f[2] };
                        e.Protocol = (ViscaProtocol)Enum.Parse(typeof(ViscaProtocol), f[1]);
                        e.Port = int.Parse(f[3], CultureInfo.InvariantCulture);
                        e.Address = int.Parse(f[4], CultureInfo.InvariantCulture);
                        ViscaCameras.Add(e);
                    }
            }
            catch { }
        }

        public string Get(string key, string def)
        {
            string v;
            return values.TryGetValue(key, out v) ? v : def;
        }

        public double GetDouble(string key, double def)
        {
            double d;
            return double.TryParse(Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        public bool GetBool(string key) { return Get(key, "") == "1"; }

        public void Set(string key, string value) { values[key] = value; }
        public void Set(string key, double value) { values[key] = value.ToString("R", CultureInfo.InvariantCulture); }
        public void Set(string key, bool value) { values[key] = value ? "1" : "0"; }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in values) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                sb.Clear();
                foreach (ViscaEntry e in ViscaCameras)
                    sb.Append(e.Label).Append('\t').Append(e.Protocol).Append('\t').Append(e.Host).Append('\t')
                      .Append(e.Port).Append('\t').Append(e.Address).Append('\n');
                File.WriteAllText(camFile, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
