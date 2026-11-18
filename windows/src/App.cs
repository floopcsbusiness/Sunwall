using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Sunwall
{
    public sealed class SunwallApp : ApplicationContext
    {
        const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        readonly Model model = new Model();
        readonly DesktopHost host = new DesktopHost();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly List<ToolStripMenuItem> presetItems = new List<ToolStripMenuItem>();
        readonly List<ToolStripMenuItem> cloudItems = new List<ToolStripMenuItem>();
        readonly List<ToolStripMenuItem> shadowItems = new List<ToolStripMenuItem>();

        Controller controller;
        System.Windows.Forms.Timer tickTimer;
        System.Windows.Forms.Timer weatherTimer;
        ToolStripMenuItem infoItem;
        ToolStripMenuItem sunItem;
        ToolStripMenuItem liveItem;
        ToolStripMenuItem loginItem;
        ToolStripMenuItem replaceItem;
        SlidersForm sliders;
        LocationForm locationForm;
        string lastRenderKey = "";
        bool menuOpen;
        bool syncing;

        // Set while the machine is asleep; nothing drawn then is ever seen.
        bool asleep;

        public SunwallApp(string[] args)
        {
            bool selfTest = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--test", StringComparison.OrdinalIgnoreCase)) selfTest = true;
            }

            model.Load();

            controller = new Controller(this);
            controller.EnsureHandle();

            tray.Icon = MakeIcon();
            tray.Text = "Sunwall";
            tray.DoubleClick += delegate { ShowSliders(); };
            tray.Visible = true;
            BuildMenu();

            Log.Write("model: preset=" + model.PresetName
                + " lat=" + model.Lat.ToString("0.####", CultureInfo.InvariantCulture)
                + " lon=" + model.Lon.ToString("0.####", CultureInfo.InvariantCulture)
                + " automatic=" + model.AutoLocation);

            host.Build();
            Redraw();
            Log.Write("startup complete");

            // The sun moves about a quarter of a degree a minute, so a minute is plenty
            // often to look at the clock; Tick() then decides whether to redraw.
            tickTimer = new System.Windows.Forms.Timer();
            tickTimer.Interval = 60000;
            tickTimer.Tick += delegate { Tick(); };
            tickTimer.Start();

            weatherTimer = new System.Windows.Forms.Timer();
            weatherTimer.Interval = 15 * 60 * 1000;
            weatherTimer.Tick += delegate { FetchWeather(); };
            weatherTimer.Start();

            if (model.AutoLocation) FetchLocation();
            FetchWeather();

            // Started with --test: paint a solid colour, then show the facts, so a
            // machine where nothing appears still reports what happened.
            if (selfTest)
            {
                Log.Write("self test requested");
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 800;
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    TestOverlay();
                    ShowDiagnostics();
                };
                t.Start();
            }
        }

        // MARK: Messages

        internal void OnMessage(ref Message m)
        {
            if (m.Msg == 0x0218) // WM_POWERBROADCAST
            {
                int ev = m.WParam.ToInt32();
                if (ev == 0x0004 || ev == 0x0006) asleep = true;      // suspend / critical
                else if (ev == 0x0007 || ev == 0x0012)                // resume
                {
                    asleep = false;
                    FetchWeather();
                    Redraw();
                }
            }
            else if (m.Msg == 0x007E) // WM_DISPLAYCHANGE
            {
                host.Build();
                Redraw();
            }
        }

        // MARK: Rendering

        string RenderKey()
        {
            DateTime date = model.Date;
            SunPos sun = Sky.Position(date, model.Lat, model.Lon);

            // Below about -8 degrees the picture is the same whatever the exact time is,
            // so the whole night folds onto one key and nothing redraws until dawn.
            string sunKey;
            if (sun.Elevation < -8) sunKey = "night";
            else
            {
                sunKey = sun.Elevation.ToString("F2", CultureInfo.InvariantCulture)
                       + "/" + sun.Azimuth.ToString("F2", CultureInfo.InvariantCulture);
            }

            return sunKey
                 + "|" + model.Lat.ToString("R", CultureInfo.InvariantCulture)
                 + "|" + model.Lon.ToString("R", CultureInfo.InvariantCulture)
                 + "|" + ((int)model.Preset).ToString(CultureInfo.InvariantCulture)
                 + "|" + model.CloudMode.ToString(CultureInfo.InvariantCulture)
                 + "|" + model.LiveCloud.ToString("R", CultureInfo.InvariantCulture)
                 + "|" + model.ShadowMode.ToString(CultureInfo.InvariantCulture)
                 + "|" + model.Brightness.ToString("R", CultureInfo.InvariantCulture)
                 + "|" + (model.ReplaceMode ? "1" : "0")
                 + "|" + host.Count.ToString(CultureInfo.InvariantCulture);
        }

        void Redraw()
        {
            lastRenderKey = RenderKey();
            host.Render(model);

            // Drawing inside the desktop window does not work everywhere. If the paint
            // never reached the screen, stop depending on Explorer's window layout.
            if (host.PaintFailed && !host.TopLevel)
            {
                Log.Write("desktop child windows refused the paint; switching to a top-level overlay");
                host.BuildTopLevel();
                host.Render(model);
            }
        }

        // Paints a solid colour through the same path the picture uses, so it is obvious
        // whether the window itself is on screen.
        void TestOverlay()
        {
            Log.Write("test overlay");
            host.Fill(Color.FromArgb(150, 255, 60, 60));
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 3000;
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                Redraw();
            };
            t.Start();
        }

        void ShowDiagnostics()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Sunwall diagnostics");
            sb.AppendLine();
            sb.Append(host.Report());
            sb.AppendLine();
            DateTime date = model.Date;
            SunPos sun = Sky.Position(date, model.Lat, model.Lon);
            sb.AppendLine("time: " + date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("location: " + model.Lat.ToString("0.####", CultureInfo.InvariantCulture)
                + ", " + model.Lon.ToString("0.####", CultureInfo.InvariantCulture)
                + (model.AutoLocation ? " (automatic)" : " (set by hand)"));
            sb.AppendLine("sun elevation: " + sun.Elevation.ToString("0.0", CultureInfo.InvariantCulture)
                + " degrees, azimuth " + sun.Azimuth.ToString("0.0", CultureInfo.InvariantCulture));
            sb.AppendLine("preset: " + model.PresetName
                + "   clouds: " + model.CloudMode
                + "   live cloud: " + model.LiveCloud.ToString("0.00", CultureInfo.InvariantCulture));
            sb.AppendLine("shadows: " + model.ShadowMode
                + "   brightness: " + model.Brightness.ToString("0.00", CultureInfo.InvariantCulture)
                + "   opacity: " + model.Opacity.ToString("0.00", CultureInfo.InvariantCulture)
                + "   full wallpaper: " + model.ReplaceMode);
            sb.AppendLine("render key: " + lastRenderKey);
            sb.AppendLine();
            sb.AppendLine("log file: " + Log.FilePath);

            Form f = new Form();
            f.Text = "Sunwall diagnostics";
            f.ClientSize = new Size(640, 440);
            f.StartPosition = FormStartPosition.CenterScreen;
            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.WordWrap = false;
            box.ScrollBars = ScrollBars.Both;
            box.Dock = DockStyle.Fill;
            box.Font = new Font("Consolas", 9f);
            box.Text = sb.ToString();
            f.Controls.Add(box);
            f.Show();
            f.Activate();
        }

        void OpenLog()
        {
            try
            {
                System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
            }
            catch (Exception) { }
        }

        void Tick()
        {
            if (asleep) return;

            // Explorer restarting takes the wallpaper window with it.
            if (!host.Valid)
            {
                host.Build();
                Redraw();
                return;
            }

            if (model.Scrub.HasValue)
            {
                if (menuOpen) UpdateLabels();
                return;
            }

            if (RenderKey() != lastRenderKey) Redraw();
            if (menuOpen) UpdateLabels();
        }

        // MARK: Menu

        void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();

            infoItem = new ToolStripMenuItem("-");
            infoItem.Enabled = false;
            menu.Items.Add(infoItem);

            sunItem = new ToolStripMenuItem("-");
            sunItem.Enabled = false;
            menu.Items.Add(sunItem);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem look = new ToolStripMenuItem("Look");
            for (int i = 0; i < Model.PresetNames.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(Model.PresetNames[i]);
                item.Click += delegate { SetPreset(index); };
                look.DropDownItems.Add(item);
                presetItems.Add(item);
            }
            menu.Items.Add(look);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem slidersItem = new ToolStripMenuItem("Sliders...");
            slidersItem.Click += delegate { ShowSliders(); };
            menu.Items.Add(slidersItem);

            liveItem = new ToolStripMenuItem("Follow real time");
            liveItem.Click += delegate
            {
                model.Scrub = null;
                model.Save();
                SyncSliders();
                Redraw();
            };
            menu.Items.Add(liveItem);
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem clouds = new ToolStripMenuItem("Clouds");
            string[] cloudNames = new string[] { "Live weather", "Clear", "Partly cloudy", "Overcast" };
            for (int i = 0; i < cloudNames.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(cloudNames[i]);
                item.Click += delegate { SetCloud(index); };
                clouds.DropDownItems.Add(item);
                cloudItems.Add(item);
            }
            menu.Items.Add(clouds);

            ToolStripMenuItem shadows = new ToolStripMenuItem("Shadows");
            string[] shadowNames = new string[] { "Off (light only)", "Soft", "Deep" };
            for (int i = 0; i < shadowNames.Length; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(shadowNames[i]);
                item.Click += delegate { SetShadow(index); };
                shadows.DropDownItems.Add(item);
                shadowItems.Add(item);
            }
            menu.Items.Add(shadows);

            replaceItem = new ToolStripMenuItem("Replace wallpaper (full mode)");
            replaceItem.Click += delegate
            {
                model.ReplaceMode = !model.ReplaceMode;
                model.Save();
                Redraw();
            };
            menu.Items.Add(replaceItem);

            ToolStripMenuItem locItem = new ToolStripMenuItem("Set location...");
            locItem.Click += delegate { ShowLocation(); };
            menu.Items.Add(locItem);

            loginItem = new ToolStripMenuItem("Launch at login");
            loginItem.Click += delegate { ToggleLogin(); };
            menu.Items.Add(loginItem);

            ToolStripMenuItem testItem = new ToolStripMenuItem("Test overlay (3 seconds)");
            testItem.Click += delegate { TestOverlay(); };
            menu.Items.Add(testItem);

            ToolStripMenuItem diagItem = new ToolStripMenuItem("Diagnostics...");
            diagItem.Click += delegate { ShowDiagnostics(); };
            menu.Items.Add(diagItem);

            ToolStripMenuItem logItem = new ToolStripMenuItem("Open log");
            logItem.Click += delegate { OpenLog(); };
            menu.Items.Add(logItem);

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem quit = new ToolStripMenuItem("Quit Sunwall");
            quit.Click += delegate { Quit(); };
            menu.Items.Add(quit);

            menu.Opening += delegate { menuOpen = true; SyncMenu(); };
            menu.Closed += delegate { menuOpen = false; };
            tray.ContextMenuStrip = menu;
        }

        void SyncMenu()
        {
            UpdateLabels();
            for (int i = 0; i < presetItems.Count; i++) presetItems[i].Checked = ((int)model.Preset) == i;
            for (int i = 0; i < cloudItems.Count; i++) cloudItems[i].Checked = model.CloudMode == i;
            for (int i = 0; i < shadowItems.Count; i++) shadowItems[i].Checked = model.ShadowMode == i;
            liveItem.Checked = !model.Scrub.HasValue;
            replaceItem.Checked = model.ReplaceMode;
            loginItem.Checked = IsLoginEnabled();
        }

        void UpdateLabels()
        {
            DateTime date = model.Date;
            SunPos sun = Sky.Position(date, model.Lat, model.Lon);
            DateTime? rise, set;
            Sky.TimesCached(date, model.Lat, model.Lon, out rise, out set);
            int sky = (int)Math.Round(model.Cloud * 100);

            infoItem.Text = date.ToString("HH:mm")
                          + "  -  sun " + ((int)Math.Round(sun.Elevation)).ToString(CultureInfo.InvariantCulture)
                          + " deg up  -  clouds " + sky.ToString(CultureInfo.InvariantCulture) + "%";

            if (rise.HasValue && set.HasValue)
            {
                sunItem.Text = "Sunrise " + rise.Value.ToString("HH:mm")
                             + "  -  Sunset " + set.Value.ToString("HH:mm");
            }
            else
            {
                sunItem.Text = "No sunrise or sunset today";
            }
        }

        void SetPreset(int index)
        {
            model.Preset = (Preset)index;
            model.Save();
            Redraw();
        }

        void SetCloud(int index)
        {
            model.CloudMode = index;
            model.Save();
            if (index == 0) FetchWeather();
            Redraw();
        }

        void SetShadow(int index)
        {
            model.ShadowMode = index;
            model.Save();
            Redraw();
        }

        // MARK: Sliders

        void ShowSliders()
        {
            if (sliders == null || sliders.IsDisposed)
            {
                sliders = new SlidersForm();
                sliders.OpacityBar.ValueChanged += delegate
                {
                    if (syncing) return;
                    model.Opacity = sliders.OpacityBar.Value / 100.0;
                    model.Save();
                    host.Repaint(model.Opacity);
                };
                sliders.BrightnessBar.ValueChanged += delegate
                {
                    if (syncing) return;
                    model.Brightness = sliders.BrightnessBar.Value / 100.0;
                    model.Save();
                    Redraw();
                };
                sliders.TimeBar.ValueChanged += delegate
                {
                    if (syncing) return;
                    model.Scrub = (double)sliders.TimeBar.Value;
                    sliders.SetTime(sliders.TimeBar.Value);
                    Redraw();
                };
                sliders.FollowClicked += delegate
                {
                    model.Scrub = null;
                    model.Save();
                    SyncSliders();
                    Redraw();
                };
            }
            SyncSliders();
            sliders.Show();
            sliders.Activate();
        }

        void SyncSliders()
        {
            if (sliders == null || sliders.IsDisposed) return;
            syncing = true;
            sliders.OpacityBar.Value = (int)Math.Round(model.Opacity * 100);
            sliders.BrightnessBar.Value = (int)Math.Round(model.Brightness * 100);
            DateTime date = model.Date;
            int minutes = date.Hour * 60 + date.Minute;
            if (minutes < 0) minutes = 0;
            if (minutes > 1439) minutes = 1439;
            sliders.TimeBar.Value = minutes;
            sliders.SetTime(minutes);
            syncing = false;
        }

        // MARK: Location

        void ShowLocation()
        {
            if (locationForm == null || locationForm.IsDisposed)
            {
                locationForm = new LocationForm();
                locationForm.Apply += delegate { ApplyLocation(); };
            }
            locationForm.LatBox.Text = model.AutoLocation ? "" : model.Lat.ToString("0.####", CultureInfo.InvariantCulture);
            locationForm.LonBox.Text = model.AutoLocation ? "" : model.Lon.ToString("0.####", CultureInfo.InvariantCulture);
            locationForm.Show();
            locationForm.Activate();
        }

        void ApplyLocation()
        {
            string la = locationForm.LatText;
            string lo = locationForm.LonText;

            if (la.Length == 0 && lo.Length == 0)
            {
                model.AutoLocation = true;
                model.Save();
                FetchLocation();
                locationForm.Close();
                return;
            }

            double a = 0, b = 0;
            bool ok = double.TryParse(la, NumberStyles.Float, CultureInfo.InvariantCulture, out a);
            if (ok) ok = double.TryParse(lo, NumberStyles.Float, CultureInfo.InvariantCulture, out b);
            if (ok) ok = Math.Abs(a) <= 90 && Math.Abs(b) <= 180;
            if (!ok)
            {
                System.Media.SystemSounds.Beep.Play();
                return;
            }

            model.AutoLocation = false;
            model.Lat = a;
            model.Lon = b;
            model.Save();
            FetchWeather();
            Redraw();
            locationForm.Close();
        }

        void FetchLocation()
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    string json = Download("https://ipapi.co/json/");
                    double la, lo;
                    if (!TryNumber(json, "\"latitude\"", out la)) return;
                    if (!TryNumber(json, "\"longitude\"", out lo)) return;
                    double gotLat = la, gotLon = lo;
                    Post(delegate
                    {
                        if (!model.AutoLocation) return; // the user typed coordinates meanwhile
                        model.Lat = gotLat;
                        model.Lon = gotLon;
                        model.Save();
                        FetchWeather();
                        Redraw();
                    });
                }
                catch (Exception) { /* No network: the fallback or saved coordinates stand. */ }
            });
        }

        // MARK: Weather (Open-Meteo, no API key)

        void FetchWeather()
        {
            if (model.CloudMode != 0) return;
            double lat = model.Lat, lon = model.Lon;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    string url = "https://api.open-meteo.com/v1/forecast?latitude="
                        + lat.ToString("0.####", CultureInfo.InvariantCulture)
                        + "&longitude=" + lon.ToString("0.####", CultureInfo.InvariantCulture)
                        + "&current=cloud_cover";
                    string json = Download(url);
                    double cc;
                    if (!TryNumber(json, "\"cloud_cover\"", out cc)) return;
                    double value = Sky.Clamp01(cc / 100.0);
                    Post(delegate { model.LiveCloud = value; Redraw(); });
                }
                catch (Exception) { }
            });
        }

        void Post(MethodInvoker action)
        {
            if (controller == null || !controller.IsHandleCreated) return;
            controller.BeginInvoke(action);
        }

        static string Download(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            using (WebClient wc = new WebClient())
            {
                wc.Headers[HttpRequestHeader.UserAgent] = "Sunwall";
                return wc.DownloadString(url);
            }
        }

        static bool TryNumber(string json, string key, out double value)
        {
            value = 0;
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return false;
            i = json.IndexOf(':', i + key.Length);
            if (i < 0) return false;
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int start = i;
            while (i < json.Length)
            {
                char c = json[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (i == start) return false;
            return double.TryParse(json.Substring(start, i - start),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // MARK: Login item

        bool IsLoginEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    return k != null && k.GetValue("Sunwall") != null;
                }
            }
            catch (Exception) { return false; }
        }

        void ToggleLogin()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return;
                    if (IsLoginEnabled()) k.DeleteValue("Sunwall", false);
                    else k.SetValue("Sunwall", "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch (Exception) { }
        }

        // MARK: Lifecycle

        void Quit()
        {
            if (tickTimer != null) tickTimer.Stop();
            if (weatherTimer != null) weatherTimer.Stop();
            tray.Visible = false;
            tray.Dispose();
            host.Destroy();
            ExitThread();
        }

        static Icon MakeIcon()
        {
            using (Bitmap b = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(b))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Color sun = Color.FromArgb(235, 255, 196, 64);
                    using (Pen p = new Pen(sun, 1.4f))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            double a = Math.PI * i / 4.0;
                            g.DrawLine(p,
                                8f + (float)(Math.Cos(a) * 6.2), 8f + (float)(Math.Sin(a) * 6.2),
                                8f + (float)(Math.Cos(a) * 7.4), 8f + (float)(Math.Sin(a) * 7.4));
                        }
                    }
                    using (SolidBrush br = new SolidBrush(sun)) g.FillEllipse(br, 3.5f, 3.5f, 9f, 9f);
                }
                IntPtr handle = b.GetHicon();
                return Icon.FromHandle(handle);
            }
        }
    }

    // A hidden top-level window: it receives the broadcast messages (power, display
    // change) that a tray icon alone does not see.
    sealed class Controller : Form
    {
        readonly SunwallApp app;

        public Controller(SunwallApp app)
        {
            this.app = app;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
        }

        public void EnsureHandle()
        {
            IntPtr h = Handle;
            GC.KeepAlive(h);
        }

        protected override void WndProc(ref Message m)
        {
            app.OnMessage(ref m);
            base.WndProc(ref m);
        }
    }

    sealed class SlidersForm : Form
    {
        public TrackBar OpacityBar;
        public TrackBar BrightnessBar;
        public TrackBar TimeBar;
        public event EventHandler FollowClicked;

        Label timeLabel;

        public SlidersForm()
        {
            Text = "Sunwall";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(280, 250);
            MaximizeBox = false;
            MinimizeBox = false;

            AddLabel("Opacity", 12);
            OpacityBar = AddBar(30, 10, 100);

            AddLabel("Brightness", 78);
            BrightnessBar = AddBar(96, 30, 180);

            timeLabel = AddLabel("Time of day", 144);
            TimeBar = AddBar(162, 0, 1439);

            Button follow = new Button();
            follow.Text = "Follow real time";
            follow.SetBounds(12, 206, 256, 30);
            follow.Click += delegate
            {
                if (FollowClicked != null) FollowClicked(this, EventArgs.Empty);
            };
            Controls.Add(follow);
        }

        Label AddLabel(string text, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(12, y, 256, 16);
            Controls.Add(l);
            return l;
        }

        TrackBar AddBar(int y, int min, int max)
        {
            TrackBar b = new TrackBar();
            b.Maximum = max;
            b.Minimum = min;
            b.TickStyle = TickStyle.None;
            b.SmallChange = 1;
            b.LargeChange = Math.Max(1, (max - min) / 10);
            b.SetBounds(12, y, 256, 36);
            Controls.Add(b);
            return b;
        }

        public void SetTime(int minutes)
        {
            timeLabel.Text = "Time of day  " + (minutes / 60).ToString("00", CultureInfo.InvariantCulture)
                           + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
        }
    }

    sealed class LocationForm : Form
    {
        public TextBox LatBox;
        public TextBox LonBox;
        public event EventHandler Apply;

        public string LatText { get { return LatBox.Text.Trim(); } }
        public string LonText { get { return LonBox.Text.Trim(); } }

        public LocationForm()
        {
            Text = "Set location";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(340, 200);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;

            Label hint = new Label();
            hint.Text = "Latitude and longitude in decimal degrees\r\n"
                      + "(west and south are negative).\r\n"
                      + "Leave both empty to find this PC's network location.";
            hint.SetBounds(12, 10, 316, 54);
            Controls.Add(hint);

            Label latLabel = new Label();
            latLabel.Text = "Latitude";
            latLabel.SetBounds(12, 72, 80, 16);
            Controls.Add(latLabel);

            LatBox = new TextBox();
            LatBox.SetBounds(96, 70, 232, 24);
            Controls.Add(LatBox);

            Label lonLabel = new Label();
            lonLabel.Text = "Longitude";
            lonLabel.SetBounds(12, 104, 80, 16);
            Controls.Add(lonLabel);

            LonBox = new TextBox();
            LonBox.SetBounds(96, 102, 232, 24);
            Controls.Add(LonBox);

            Button save = new Button();
            save.Text = "Save";
            save.SetBounds(154, 150, 84, 28);
            save.Click += delegate
            {
                if (Apply != null) Apply(this, EventArgs.Empty);
            };
            Controls.Add(save);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.SetBounds(244, 150, 84, 28);
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }
    }
}
