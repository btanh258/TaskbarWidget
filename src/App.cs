using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BatteryBar
{
    static class Program
    {
        public const string Version = "1.2.20";
        public const string Repo = "https://github.com/btanh258/TaskbarWidget";
        const string MutexName = "TaskbarWidget.Single";
        public const string ShowEvent = "TaskbarWidget.ShowSettings";
        public const string ExitEvent = "TaskbarWidget.Exit";

        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TaskbarWidget"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "TaskbarWidget.exe"); } }
        public static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Taskbar Widget.lnk"); } }
        public static bool IsInstalledCopy { get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase); } }

        [STAThread]
        static void Main(string[] args)
        {
            // TaskbarWidget.exe --dump file.txt : write what every source sees, then quit
            if (args.Length >= 2 && args[0] == "--dump")
            {
                var p = new Poller();
                p.PollAll(true);
                var sys = new SystemMonitor();
                sys.Poll();
                File.WriteAllText(args[1], p.Log + "\r\n" + sys.Log);
                return;
            }
            // TaskbarWidget.exe /install-pawnio : the elevated copy installs
            // the PawnIO driver, then restarts the app unelevated and exits.
            // It runs before the single-instance mutex so the install never
            // fights the running copy.
            if (args.Contains("/install-pawnio"))
            {
                if (PawnIo.IsAdmin())
                {
                    if (!PawnIo.Present())
                    {
                        string why;
                        if (!PawnIo.Install(out why))
                            MessageBox.Show(why, "Taskbar Widget – PawnIO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    PawnIo.RestartUnelevated();
                }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool welcome = args.Contains("--welcome");
            Poller.Demo = args.Contains("--demo");
            if (!IsInstalledCopy && !args.Contains("--portable"))
            {
                var answer = MessageBox.Show(
                    "Install Taskbar Widget for this Windows user?\n\n" +
                    "Yes: install, start with Windows, add it to the Start menu.\n" +
                    "No: just run it from here this time.",
                    "Taskbar Widget " + Version, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes)
                {
                    try { Installer.Install(); }
                    catch (Exception e) { MessageBox.Show("Install failed: " + e.Message, "Taskbar Widget"); }
                    return;
                }
                welcome = true;
            }

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) { Native.AllowSetForegroundWindow(-1); Signal(ShowEvent); return; }
                Application.Run(new AppContext(welcome));
            }
        }

        public static void Signal(string name)
        {
            try { using (var e = EventWaitHandle.OpenExisting(name)) e.Set(); } catch { }
        }

        public static bool Running()
        {
            try { using (Mutex.OpenExisting(MutexName)) return true; } catch { return false; }
        }
    }

    static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void Install()
        {
            Program.Signal(Program.ExitEvent); // stop an older running copy
            for (int i = 0; i < 50 && Program.Running(); i++) Thread.Sleep(100);
            Directory.CreateDirectory(Program.InstallDir);
            File.Copy(Application.ExecutablePath, Program.InstalledExe, true);
            SetStartup(true, Program.InstalledExe);
            MakeShortcut();
            Process.Start(Program.InstalledExe, "--welcome");
        }

        public static bool StartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("TaskbarWidget") != null;
        }

        public static void SetStartup(bool on, string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue("TaskbarWidget", "\"" + exe + "\"");
                else if (k.GetValue("TaskbarWidget") != null) k.DeleteValue("TaskbarWidget");
            }
        }

        static void MakeShortcut()
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { Program.Shortcut });
                Type lt = lnk.GetType();
                lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Program.InstalledExe });
                lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "Battery levels and system stats on the taskbar" });
                lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
        }

        // Remove startup entry, shortcut, settings and (after exit) the installed files.
        public static void Uninstall()
        {
            SetStartup(false, null);
            try { File.Delete(Program.Shortcut); } catch { }
            try { Directory.Delete(Settings.Dir, true); } catch { }
            if (Directory.Exists(Program.InstallDir))
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + Program.InstallDir + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
            }
        }
    }

    class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarWidget");
        static string FilePath { get { return Path.Combine(Dir, "settings.txt"); } }

        public int Interval = 60;      // seconds between battery checks
        public int SysInterval = 2;    // seconds between system checks (TraySense default)
        public bool ShowBluetooth = true; // show the battery/Bluetooth cluster
        public bool BtLeft = false;    // Bluetooth cluster: true = left edge of taskbar, false = tray side
        public bool ShowSystem = true; // show CPU/RAM/GPU/... on the taskbar
        public bool SysLeft = false;   // System cluster: true = left edge of taskbar, false = tray side
        public float FontSize = 12;    // px at 100% scaling
        public int Offset = 0;         // extra px to the left of the tray
        public bool PercentSign = true;
        public bool AutoUpdate = true; // check GitHub releases for updates
        public bool AskPawnIo = true; // offer the PawnIO driver install once (1.2.19)
        public readonly HashSet<string> Hidden = new HashSet<string>();
        public readonly Dictionary<string, string> Kind = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    // First run: mirror TraySense's default mask. On: CPU,
                    // GPU, RAM, SSD temp, Network, CPU/GPU power, GPU/VRAM
                    // clock (clocks merged into the GPU/VRAM strip items
                    // since 1.2.14). Off (still listed in the panel, unhide
                    // with the eye): VRAM, disk util.
                    foreach (string k in new[] { "sys:vram", "sys:disk" }) s.Hidden.Add(k);
                    return s;
                }
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] p = line.Split('\t');
                    if (p.Length < 2) continue;
                    switch (p[0])
                    {
                        case "interval": s.Interval = Math.Max(10, int.Parse(p[1])); break;
                        case "sysinterval": s.SysInterval = Math.Max(1, Math.Min(60, int.Parse(p[1]))); break;
                        case "btshow": s.ShowBluetooth = p[1] == "1"; break;
                        case "btleft": s.BtLeft = p[1] == "1"; break;
                        case "sysshow": s.ShowSystem = p[1] == "1"; break;
                        case "sysleft": s.SysLeft = p[1] == "1"; break;
                        case "font": s.FontSize = Math.Max(8, float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)); break;
                        case "offset": s.Offset = int.Parse(p[1]); break;
                        case "percent": s.PercentSign = p[1] == "1"; break;
                        case "update": s.AutoUpdate = p[1] == "1"; break;
                        case "pioask": s.AskPawnIo = p[1] == "1"; break;
                        case "hide": s.Hidden.Add(p[1]); break;
                        case "kind": if (p.Length > 2) s.Kind[p[1]] = p[2]; break;
                        case "name": if (p.Length > 2) s.Names[p[1]] = p[2]; break;
                    }
                }
            }
            catch { }
            // 1.1.0 / 1.2.5: CPU/GPU temperature merged into the usage item,
            // so the old sys:cput / sys:gput rows are gone for good
            // (otherwise they would linger as "Not connected" in the panel).
            s.Names.Remove("sys:cput");
            s.Kind.Remove("sys:cput");
            s.Hidden.Remove("sys:cput");
            s.Names.Remove("sys:gput");
            s.Kind.Remove("sys:gput");
            s.Hidden.Remove("sys:gput");
            // 1.2.7: GPU power is shown merged into the GPU item now, so
            // unhide it on upgrade (it was hidden by default before).
            s.Hidden.Remove("sys:gpupower");
            // 1.2.14: GPU/VRAM clock are shown merged into the GPU/VRAM
            // items now, so unhide them on upgrade too.
            s.Hidden.Remove("sys:gpuclk");
            s.Hidden.Remove("sys:memclk");
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                lines.Add("interval\t" + Interval);
                lines.Add("sysinterval\t" + SysInterval);
                lines.Add("btshow\t" + (ShowBluetooth ? "1" : "0"));
                lines.Add("btleft\t" + (BtLeft ? "1" : "0"));
                lines.Add("sysshow\t" + (ShowSystem ? "1" : "0"));
                lines.Add("sysleft\t" + (SysLeft ? "1" : "0"));
                lines.Add("font\t" + FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
                lines.Add("offset\t" + Offset);
                lines.Add("percent\t" + (PercentSign ? "1" : "0"));
                lines.Add("update\t" + (AutoUpdate ? "1" : "0"));
                lines.Add("pioask\t" + (AskPawnIo ? "1" : "0"));
                foreach (var h in Hidden) lines.Add("hide\t" + h);
                foreach (var k in Kind) lines.Add("kind\t" + k.Key + "\t" + k.Value);
                foreach (var n in Names) lines.Add("name\t" + n.Key + "\t" + n.Value);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }

    class AppContext : ApplicationContext
    {
        public readonly Settings Settings = Settings.Load();
        public readonly Poller Poller = new Poller();
        public readonly SystemMonitor Sys = new SystemMonitor();
        public readonly Updater Updater = new Updater();
        public readonly Dictionary<string, Reading> Latest = new Dictionary<string, Reading>();
        readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>();
        readonly Control ui = new Control();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly AutoResetEvent sysWake = new AutoResetEvent(false);
        Bar btBar, sysBar; // one strip window per cluster
        List<Reading> shownBt = new List<Reading>(), shownSys = new List<Reading>();
        Flyout form;
        public DateTime LastPoll = DateTime.MinValue;
        bool polledOnce;

        public AppContext(bool welcome)
        {
            ui.CreateControl();
            IntPtr force = ui.Handle;

            timer.Interval = 1000;
            timer.Tick += delegate { KeepBar(); };
            timer.Start();
            KeepBar();

            var poll = new Thread(PollLoop);
            poll.IsBackground = true;
            poll.Start();

            // System stats refresh every few seconds (TraySense: 2s), on their
            // own thread: the battery interval (60s) would be far too slow,
            // and polling HID devices every 2s would be far too fast.
            var sysPoll = new Thread(SystemLoop);
            sysPoll.IsBackground = true;
            sysPoll.Start();

            var events = new Thread(WaitEvents);
            events.IsBackground = true;
            events.Start();

            Updater.Start(this);

            // PawnIO: if CPU temperature/power are wanted but the driver is
            // missing, offer to install it once, shortly after startup
            // (TraySense asked the same way when CPU temp was enabled).
            var pioTimer = new System.Windows.Forms.Timer();
            pioTimer.Interval = 8000;
            pioTimer.Tick += delegate
            {
                pioTimer.Stop();
                MaybeAskPawnIo();
            };
            pioTimer.Start();

            if (welcome) ShowSettings();
            if (Environment.GetCommandLineArgs().Contains("--demo-settings")) { form = new Flyout(this); form.Open(true); }
        }

        public void Refresh() { wake.Set(); sysWake.Set(); }
        public void RefreshSystem() { sysWake.Set(); }
        public void UiBeginInvoke(Action a) { try { ui.BeginInvoke(a); } catch { } }

        void MaybeAskPawnIo()
        {
            if (Poller.Demo) return;
            if (!Settings.AskPawnIo) return;
            if (!Settings.ShowSystem || Settings.Hidden.Contains("sys:cpu")) return;
            if (PawnIo.Present()) return;
            AskPawnIoInstall();
        }

        // The Settings pill calls this directly; the startup check goes
        // through MaybeAskPawnIo so a decline is only asked once.
        public void AskPawnIoInstall()
        {
            if (PawnIo.Present()) return;
            var r = MessageBox.Show(
                "Taskbar Widget needs the PawnIO driver to read CPU temperature and CPU power.\n\n" +
                "PawnIO is a small, signed, open-source driver (the one LibreHardwareMonitor uses). " +
                "It is downloaded from GitHub and verified before it runs, and can be removed any time from Settings > Apps.\n\n" +
                "Windows will ask for permission. Install it now?",
                "Taskbar Widget – CPU temperature", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { Settings.AskPawnIo = false; Settings.Save(); return; }
            if (PawnIo.RelaunchAdmin("/install-pawnio")) ExitThread();
        }

        void SystemLoop()
        {
            while (true)
            {
                List<Reading> list;
                try { list = Sys.Poll(); }
                catch (Exception e) { list = new List<Reading>(); Sys.Log += "system poll failed: " + e + "\r\n"; }
                try { ui.BeginInvoke(new Action(() => Apply(list))); } catch { return; }
                sysWake.WaitOne(Settings.SysInterval * 1000);
            }
        }

        void PollLoop()
        {
            while (true)
            {
                List<Reading> list;
                try { list = Poller.PollAll(false); }
                catch (Exception e) { list = new List<Reading>(); Poller.Log += "poll failed: " + e + "\r\n"; }
                try { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(Path.Combine(Settings.Dir, "last-check.txt"), Poller.Log + "\r\n" + Sys.Log); } catch { }
                try { ui.BeginInvoke(new Action(() => Apply(list))); } catch { return; }
                wake.WaitOne(Settings.Interval * 1000);
            }
        }

        void WaitEvents()
        {
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEvent))
            using (var exit = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEvent))
            {
                while (true)
                {
                    int i = WaitHandle.WaitAny(new WaitHandle[] { show, exit });
                    try
                    {
                        if (i == 0) ui.BeginInvoke(new Action(ShowSettings));
                        else { ui.BeginInvoke(new Action(ExitThread)); return; }
                    }
                    catch { return; }
                }
            }
        }

        void Apply(List<Reading> list)
        {
            polledOnce = true;
            LastPoll = DateTime.Now;
            bool newName = false;
            foreach (var r in list)
            {
                Latest[r.Key] = r;
                seen[r.Key] = DateTime.UtcNow;
                string old;
                if (!Settings.Names.TryGetValue(r.Key, out old) || old != r.Name) { Settings.Names[r.Key] = r.Name; newName = true; }
            }
            if (newName && !Poller.Demo) Settings.Save();
            try { Redraw(); }
            catch (Exception e) { try { File.AppendAllText(Path.Combine(Settings.Dir, "last-check.txt"), "redraw failed: " + e + "\r\n"); } catch { } }
            if (form != null && !form.IsDisposed) form.Refill();
        }

        public string KindOf(Reading r)
        {
            string k;
            return Settings.Kind.TryGetValue(r.Key, out k) ? k : r.Kind;
        }

        // Readings that are fresh (a missed check or two is tolerated) and not hidden.
        public List<Reading> Fresh()
        {
            // devices nap (mice sleep after a few idle minutes): keep the last value a while
            var keep = TimeSpan.FromSeconds(Math.Max(Settings.Interval * 2.5 + 10, 1800));
            return Latest.Values.Where(r => DateTime.UtcNow - seen[r.Key] < keep).ToList();
        }

        public void Redraw()
        {
            var bt = new List<Reading>();
            var sys = new List<Reading>();
            foreach (var r in Fresh())
            {
                if (Settings.Hidden.Contains(r.Key)) continue;
                var c = new Reading { Key = r.Key, Name = r.Name, Pct = r.Pct, Charging = r.Charging, Source = r.Source, Kind = KindOf(r), Text = r.Text, Value = r.Value, Detail = r.Detail, Barless = r.Barless };
                if (Kinds.IsSystem(c.Kind)) { if (Settings.ShowSystem) sys.Add(c); }
                else if (Settings.ShowBluetooth) bt.Add(c);
            }
            // Bluetooth sorts by name, not by icon kind: changing an icon
            // must not move the item on the strip. System keeps its fixed
            // kind order (system kinds never change).
            bt.Sort((a, b) => { int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.Ordinal); });
            sys.Sort((a, b) => Kinds.Order(a.Kind) != Kinds.Order(b.Kind) ? Kinds.Order(a.Kind) - Kinds.Order(b.Kind) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            // Power and clocks are their own rows in the panel (own eye
            // toggle), but on the strip they join their usage item:
            // "CPU 12% 54° 20W", "GPU 4% 44° 120W 2100MHz",
            // "VRAM 6% 0.5/8.0 GB 1800MHz". Hidden extras are already
            // filtered out above, so they only merge when shown.
            MergeInto(sys, "cpu", "cpupower");
            MergeInto(sys, "gpu", "gpupower");
            MergeInto(sys, "gpu", "gpuclk");
            MergeInto(sys, "vram", "memclk");
            shownBt = bt;
            shownSys = sys;
            UpdateBar(ref btBar, bt);
            UpdateBar(ref sysBar, sys);
            LayoutBars();
        }

        // Append a visible extra reading (power, clock) to its usage
        // item's strip text, and drop the standalone extra item. If the
        // usage item is missing the extra item stays on its own.
        static void MergeInto(List<Reading> sys, string usageKind, string extraKind)
        {
            Reading extra = null, usage = null;
            foreach (var r in sys)
            {
                if (r.Kind == extraKind) extra = r;
                else if (r.Kind == usageKind) usage = r;
            }
            if (extra == null || usage == null) return;
            usage.Text += " " + extra.Value;
            sys.Remove(extra);
        }

        // Feed one cluster's strip; destroy it when the cluster is empty
        // (before the first poll the Bluetooth strip shows "--" instead).
        void UpdateBar(ref Bar b, List<Reading> items)
        {
            if (items.Count == 0)
            {
                if (!polledOnce) { if (b != null) b.SetItems(items); return; }
                if (b != null) { b.Destroy(); b = null; }
                return;
            }
            if (b != null) b.SetItems(items);
        }

        // Place the two clusters: each sits on the left edge of the taskbar
        // or on the tray side, per its Position setting. On a shared side
        // Bluetooth is always left of System.
        public void LayoutBars()
        {
            if (btBar == null && sysBar == null) return;
            float s = Scale();
            int gap = (int)(12 * s);
            int off = (int)(Settings.Offset * s);
            int trayLeft = (btBar ?? sysBar).TrayLeft();
            int rightEdge = trayLeft - off;
            int leftEdge = off;
            int cursor = rightEdge;
            if (sysBar != null && !Settings.SysLeft) { sysBar.OnLeft = false; sysBar.Anchor = cursor; cursor -= sysBar.Width + gap; }
            if (btBar != null && !Settings.BtLeft) { btBar.OnLeft = false; btBar.Anchor = cursor; cursor -= btBar.Width + gap; }
            cursor = leftEdge;
            if (btBar != null && Settings.BtLeft) { btBar.OnLeft = true; btBar.Anchor = cursor; cursor += btBar.Width + gap; }
            if (sysBar != null && Settings.SysLeft) { sysBar.OnLeft = true; sysBar.Anchor = cursor; cursor += sysBar.Width + gap; }
            if (btBar != null) btBar.Place();
            if (sysBar != null) sysBar.Place();
        }

        void KeepBar()
        {
            IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
            if (tb == IntPtr.Zero) return;
            bool btNeed = !polledOnce || (Settings.ShowBluetooth && shownBt.Count > 0);
            bool sysNeed = Settings.ShowSystem && shownSys.Count > 0;
            bool made = EnsureBar(ref btBar, tb, btNeed) | EnsureBar(ref sysBar, tb, sysNeed);
            if (made) Redraw();
            else LayoutBars();
        }

        // Create a cluster's strip when needed, destroy it when not (or when
        // the taskbar died). Returns true when a strip was (re)created.
        bool EnsureBar(ref Bar b, IntPtr tb, bool need)
        {
            if (b == null || b.Taskbar != tb || !b.Alive)
            {
                if (b != null) b.Destroy();
                b = null;
                if (!need) return false;
                try
                {
                    b = new Bar(tb, Settings);
                    // Left click on a strip does nothing; right click opens
                    // the panel. (The old right-click menu is gone: Refresh
                    // and Quit live as buttons inside the panel.)
                    b.RightClick += delegate { ToggleFlyout(); };
                    return true;
                }
                catch { b = null; return false; }
            }
            if (!need) { b.Destroy(); b = null; }
            return false;
        }

        public void ShowSettings()
        {
            if (form == null || form.IsDisposed) form = new Flyout(this);
            form.Open(false);
        }

        // Clicking the strip opens the flyout; clicking it again (which first closes it) does not reopen.
        void ToggleFlyout()
        {
            if (form != null && !form.IsDisposed && (form.Visible || DateTime.UtcNow - form.HiddenAt < TimeSpan.FromMilliseconds(400))) { form.Hide(); return; }
            ShowSettings();
        }

        public float Scale()
        {
            IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
            uint d = tb == IntPtr.Zero ? 96 : Native.GetDpiForWindow(tb);
            return (d == 0 ? 96 : d) / 96f;
        }

        public bool BarRect(out Native.RECT r)
        {
            r = new Native.RECT();
            bool any = false;
            Native.RECT t;
            if (btBar != null && btBar.Handle != IntPtr.Zero && Native.GetWindowRect(btBar.Handle, out t)) { r = t; any = true; }
            if (sysBar != null && sysBar.Handle != IntPtr.Zero && Native.GetWindowRect(sysBar.Handle, out t))
            {
                if (!any) { r = t; any = true; }
                else { r.Left = Math.Min(r.Left, t.Left); r.Right = Math.Max(r.Right, t.Right); r.Top = Math.Min(r.Top, t.Top); r.Bottom = Math.Max(r.Bottom, t.Bottom); }
            }
            return any;
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            if (btBar != null) btBar.Destroy();
            if (sysBar != null) sysBar.Destroy();
            if (form != null && !form.IsDisposed) form.Close();
            base.ExitThreadCore();
        }
    }
}
