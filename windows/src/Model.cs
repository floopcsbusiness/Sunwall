using System;
using System.Globalization;
using Microsoft.Win32;

namespace Sunwall
{
    public enum Preset
    {
        Olive, Blinds, Prism, Suncatcher, Arch, Komorebi,
        Tidal, Rainlight, Copper, Opaline, Jali, Shoji
    }

    // Everything the app remembers between runs, plus the derived values the renderer
    // asks for. Kept next to the drawing code the same way the Swift Model is.
    public sealed class Model
    {
        public static readonly string[] PresetNames = new string[]
        {
            "Olive", "Blinds", "Prism", "Suncatcher", "Arch", "Komorebi",
            "Tidal", "Rainlight", "Copper", "Opaline", "Jali", "Shoji"
        };

        const string RegPath = @"Software\Sunwall";

        public Preset Preset = Preset.Blinds;

        // Fallback until a location is known or entered (Richmond, VA area).
        public double Lat = 37.54;
        public double Lon = -77.43;

        public bool AutoLocation = true;
        public double? Scrub;          // minutes since local midnight; null = live
        public int CloudMode;          // 0 live, 1 clear, 2 partly, 3 overcast
        public int ShadowMode = 1;     // 0 off, 1 soft, 2 deep
        public double Brightness = 1.0;   // 0.3 ... 1.8
        public double Opacity = 1.0;      // 0.1 ... 1
        public bool ReplaceMode;       // false = overlay on wallpaper, true = solid wall
        public double LiveCloud;       // 0 ... 1 from the weather API

        public DateTime Date
        {
            get
            {
                if (Scrub.HasValue) return DateTime.Today.AddMinutes(Scrub.Value);
                return DateTime.Now;
            }
        }

        public double ShadowDepth
        {
            get
            {
                switch (ShadowMode)
                {
                    case 1: return 0.30;
                    case 2: return 0.50;
                    default: return 0;
                }
            }
        }

        public double Cloud
        {
            get
            {
                switch (CloudMode)
                {
                    case 1: return 0;
                    case 2: return 0.5;
                    case 3: return 0.95;
                    default: return LiveCloud;
                }
            }
        }

        public string PresetName { get { return PresetNames[(int)Preset]; } }

        public void SetPresetByName(string name)
        {
            int i = Array.IndexOf(PresetNames, name);
            if (i >= 0) Preset = (Preset)i;
        }

        public void Load()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath))
                {
                    if (k == null) return;
                    string s = k.GetValue("preset") as string;
                    if (s != null) SetPresetByName(s);
                    Lat = ReadDouble(k, "lat", Lat);
                    Lon = ReadDouble(k, "lon", Lon);
                    AutoLocation = ReadInt(k, "autoLocation", AutoLocation ? 1 : 0) != 0;
                    CloudMode = ReadInt(k, "cloudMode", CloudMode);
                    ShadowMode = ReadInt(k, "shadowMode", ShadowMode);
                    Brightness = ReadDouble(k, "brightness", Brightness);
                    Opacity = ReadDouble(k, "opacity", Opacity);
                    ReplaceMode = ReadInt(k, "replaceMode", ReplaceMode ? 1 : 0) != 0;
                }
            }
            catch (Exception) { /* First run, or a locked registry: defaults stand. */ }
        }

        public void Save()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
                {
                    if (k == null) return;
                    k.SetValue("preset", PresetName, RegistryValueKind.String);
                    k.SetValue("lat", Lat.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    k.SetValue("lon", Lon.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    k.SetValue("autoLocation", AutoLocation ? 1 : 0, RegistryValueKind.DWord);
                    k.SetValue("cloudMode", CloudMode, RegistryValueKind.DWord);
                    k.SetValue("shadowMode", ShadowMode, RegistryValueKind.DWord);
                    k.SetValue("brightness", Brightness.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    k.SetValue("opacity", Opacity.ToString("R", CultureInfo.InvariantCulture), RegistryValueKind.String);
                    k.SetValue("replaceMode", ReplaceMode ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { /* Nothing worth failing over. */ }
        }

        static double ReadDouble(RegistryKey k, string name, double fallback)
        {
            object v = k.GetValue(name);
            if (v == null) return fallback;
            if (v is string)
            {
                double d;
                if (double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return fallback;
            }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        static int ReadInt(RegistryKey k, string name, int fallback)
        {
            object v = k.GetValue(name);
            if (v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }
    }
}
