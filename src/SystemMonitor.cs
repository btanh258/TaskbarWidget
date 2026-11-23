// System monitor ported from TraySense 1.2 (traysense.c, AGPL-3.0, DMTWEAKS).
// Same metrics, same formulas, same thresholds as TraySense:
//   CPU %  = GetSystemTimes delta            (read_cpu)
//   RAM %  = GlobalMemoryStatusEx            (read_ram)
//   GPU %  = PDH \GPU Engine(*)\Utilization Percentage, grouped by LUID,
//            summed per engine, busiest engine wins   (read_gpu_usage)
//   VRAM % = PDH \GPU Adapter Memory(*)\Dedicated Usage / DXGI total,
//            total here from Win32_VideoController.AdapterRAM (read_vram)
//   SSD temp = IOCTL_STORAGE_QUERY_PROPERTY on the Windows drive (read_disk):
//            StorageDeviceTemperatureProperty first, else the NVMe SMART
//            log page 2 (composite temp, Kelvin) — the property stays empty
//            on many NVMe drives, notably in mini PCs (new in 1.2.14) —
//            else classic ATA SMART attribute 0xC2/0xBE (new in 1.2.16),
//            else WMI MSStorageDriver_ATAPISmartData (new in 1.2.17).
//            On Bạch's machine even the plain drive-model query fails,
//            so the storage driver itself (likely RAID mode) is refusing
//            direct queries; --dump now lists every path's reason.
//   Disk util = 100 - PDH \PhysicalDisk(*)\% Idle Time on the Windows drive
//   CPU temp = PawnIO MSR/SMN when the PawnIO driver is already installed
//            (pio_temp), else PDH \Thermal Zone Information(*)\Temperature
//            fallback, Kelvin -> C   (read_therm)
//   CPU power = RAPL package energy MSR via PawnIO (read_cpu_power):
//            Intel unit 0x606 / energy 0x611, AMD 0xC0010299 /
//            0xC001029B; watts = energy delta / time (new in 1.2.7).
//   GPU temp = D3DKMT ADAPTERPERFDATA (Temperature/10) first — works for
//            AMD, NVIDIA and Intel — then NVML (read_gpu_temp). GPU power
//            = NVML, else ADL PMLog board/ASIC power (watts), else D3DKMT
//            Power/10. GPU clocks = NVML, else ADL PMLog sensors 1 (GPU)
//            / 2 (VRAM). On AMD APUs D3DKMT's Power field is a fixed
//            placeholder, so ADL is what makes the wattage move there
//            (new in 1.2.10). Readings are simply omitted when no sensor
//            answers, exactly like TraySense's g_ok flag.
//   Network = PDH \Network Interface(*)\Bytes Received/Sent per sec,
//            summed over every interface except loopback (new in 1.2.5,
//            not a TraySense metric).
//   Speed test, hover graph and tray-icon mode are NOT ported in v1.
// C# 5 only, like the rest of this project: no interpolated strings,
// no null-conditional operator, no nameof.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BatteryBar
{
    class SystemMonitor
    {
        public string Log = "";

        readonly object gate = new object();

        // --- CPU % (GetSystemTimes delta) ---
        ulong prevIdle, prevTotal;
        bool havePrevCpu;

        // --- PDH via PerformanceCounter (cached, first sample is always 0) ---
        readonly Dictionary<string, PerformanceCounter> gpuCounters = new Dictionary<string, PerformanceCounter>();
        readonly Dictionary<string, PerformanceCounter> vramCounters = new Dictionary<string, PerformanceCounter>();
        readonly List<PerformanceCounter> thermCounters = new List<PerformanceCounter>();
        PerformanceCounter diskCounter;
        string diskInstance;

        // --- Network throughput (all interfaces except loopback) ---
        readonly List<PerformanceCounter> netRecv = new List<PerformanceCounter>();
        readonly List<PerformanceCounter> netSent = new List<PerformanceCounter>();
        bool netInit;
        DateTime counterRefresh = DateTime.MinValue;
        string chosenLuid;

        // --- WMI (queried once) ---
        bool wmiTried;
        ulong vramTotal;
        string gpuName, cpuVendor;

        // --- SSD temp (Windows drive handle, opened once) ---
        IntPtr diskHandle = IntPtr.Zero;
        bool diskHandleTried;
        int diskNumber = -1;
        string diskModel;
        bool diskModelTried;
        bool wmiSmartTried, wmiSmartOk;
        string wmiSmartWhy = "";

        // --- PawnIO (tried once) ---
        const uint IoctlPioLoad = 0xA1B22084;
        const uint IoctlPioExec = 0xA1B22104;
        int pioState; // 0 untried, 1 ok, 2 no driver, 3 failed, 4 unsupported
        IntPtr pioHandle = IntPtr.Zero;
        int pioVendor; // 1 Intel, 2 AMD
        int tjMax = 100;
        Mutex pciMutex;

        // --- CPU power (RAPL energy counter, first sample primes it) ---
        double cpuPowerUnit;
        ulong lastCpuEnergy;
        DateTime lastCpuPowerAt;

        // --- NVML (tried once) ---
        bool nvmlTried;
        IntPtr nvmlDev = IntPtr.Zero;
        NvmlInitFn nvmlInit;
        NvmlHandleFn nvmlHandle;
        NvmlTempFn nvmlTemp;
        NvmlPowerFn nvmlPower;
        NvmlClockFn nvmlClock;

        // --- D3DKMT (GPU temp/power via the display driver) ---
        bool kmtTried, kmtOk;
        uint kmtAdapter;
        string gpuTempWhy = "";
        string ssdTempWhy = "";
        string gpuPowerSrc = "";

        // --- ADL (AMD GPU power/clocks via atiadlxx.dll) ---
        bool adlTried;
        IntPtr adlCtx = IntPtr.Zero;
        int adlIndex = -1;
        AdlMallocFn adlMalloc;
        AdlCreateFn adlCreate;
        AdlCountFn adlCount;
        AdlPmLogFn adlPmLog;
        readonly byte[] adlBuf = new byte[2052];
        // One ADL driver call per poll: power + both clocks share it.
        bool adlFresh;
        int adlCachedIndex = -1;

        public List<Reading> Poll()
        {
            lock (gate)
            {
                // The ADL query cache below is per poll: power and both
                // clocks share one driver call instead of three.
                adlFresh = false;
                adlCachedIndex = -1;
                var list = new List<Reading>();
                var log = new StringBuilder();
                log.AppendLine("System monitor at " + DateTime.Now);
                if (Poller.Demo)
                {
                    list.Add(Make("sys:cpu", "CPU usage", "cpu", 12, "CPU 12% 54°", "12% 54°", null, false));
                    list.Add(Make("sys:cpupower", "CPU power", "cpupower", 0, "CPU 20W", "20W", null, true));
                    list.Add(Make("sys:gpu", "GPU usage", "gpu", 4, "GPU 4% 44°", "4% 44°", null, false));
                    list.Add(Make("sys:gpupower", "GPU power", "gpupower", 0, "GPU 120W", "120W", null, true));
                    list.Add(Make("sys:gpuclk", "GPU clock", "gpuclk", 0, "GPU 2100MHz", "2100MHz", null, true));
                    list.Add(Make("sys:ram", "RAM usage", "ram", 61, "RAM 61% 9.8/16.0 GB", "61%", "9.8 / 16.0 GB", false));
                    list.Add(Make("sys:vram", "VRAM usage", "vram", 6, "VRAM 6% 0.5/8.0 GB", "6%", "0.5 / 8.0 GB", false));
                    list.Add(Make("sys:memclk", "VRAM clock", "memclk", 0, "VRAM 1800MHz", "1800MHz", null, true));
                    list.Add(Make("sys:disk", "Storage utilization", "disk", 12, "Disk 12%", "12%", null, false));
                    list.Add(Make("sys:ssd", "SSD temperature", "ssd", 38, "SSD 38°", "38°", null, true));
                    list.Add(Make("sys:net", "Network", "net", 0, "NET ↓1.2M ↑320K", "↓1.2M ↑320K", "Down 1.2 MB/s · Up 320 KB/s", true));
                    Log = "demo mode";
                    return list;
                }

                // CPU usage + temperature are one item: "CPU 12% 54°"
                // (temperature only when a sensor answers).
                double cpuTemp;
                bool hasCpuTemp = CpuTemp(out cpuTemp);
                double cpu = CpuPct();
                if (cpu >= 0)
                {
                    int ci = (int)Math.Round(cpu);
                    string text = "CPU " + ci + "%" + (hasCpuTemp ? " " + (int)Math.Round(cpuTemp) + "°" : "");
                    string value = ci + "%" + (hasCpuTemp ? " " + (int)Math.Round(cpuTemp) + "°" : "");
                    list.Add(Make("sys:cpu", "CPU usage", "cpu", ci, text, value, hasCpuTemp ? TempNote(cpuTemp, 80, 90) : null, false));
                    log.AppendLine("  cpu: " + cpu.ToString("F1") + "%" + (hasCpuTemp ? " " + cpuTemp.ToString("F0") + "C" : ""));
                }
                double cpuW;
                if (CpuPower(out cpuW))
                {
                    list.Add(Make("sys:cpupower", "CPU power", "cpupower", 0, "CPU " + (int)Math.Round(cpuW) + "W", (int)Math.Round(cpuW) + "W", null, true));
                    log.AppendLine("  cpu power: " + cpuW.ToString("F1") + "W");
                }
                double ram;
                double ramUsed, ramTotal;
                if (RamPct(out ram, out ramUsed, out ramTotal))
                {
                    list.Add(Make("sys:ram", "RAM usage", "ram", (int)Math.Round(ram),
                        "RAM " + (int)Math.Round(ram) + "% " + ramUsed.ToString("F1") + "/" + ramTotal.ToString("F1") + " GB",
                        (int)Math.Round(ram) + "%",
                        ramUsed.ToString("F1") + " / " + ramTotal.ToString("F1") + " GB", false));
                    log.AppendLine("  ram: " + ram.ToString("F1") + "% (" + ramUsed.ToString("F1") + " / " + ramTotal.ToString("F1") + " GB)");
                }
                // GPU usage + temperature are one item: "GPU 4% 44°"
                // (temperature only when a sensor answers), like the CPU.
                // Usage first: it finds the adapter LUID the temperature
                // path (D3DKMT) needs to open the adapter.
                double gpu = GpuPct();
                double gpuTemp;
                bool hasGpuTemp = GpuTemp(out gpuTemp);
                if (gpu >= 0)
                {
                    int gi = (int)Math.Round(gpu);
                    string gtext = "GPU " + gi + "%" + (hasGpuTemp ? " " + (int)Math.Round(gpuTemp) + "°" : "");
                    string gvalue = gi + "%" + (hasGpuTemp ? " " + (int)Math.Round(gpuTemp) + "°" : "");
                    string hot = hasGpuTemp ? TempNote(gpuTemp, 80, 90) : null;
                    list.Add(Make("sys:gpu", "GPU usage", "gpu", gi, gtext, gvalue, hot ?? GpuName(), false));
                    log.AppendLine("  gpu: " + gpu.ToString("F1") + "%" + (hasGpuTemp ? " " + gpuTemp.ToString("F0") + "C" : " (no temp: " + gpuTempWhy + ")") + (GpuName() == null ? "" : " (" + GpuName() + ")"));
                }
                double vramUsed;
                double vramPct;
                if (Vram(out vramUsed, out vramPct))
                {
                    if (vramPct >= 0)
                        list.Add(Make("sys:vram", "VRAM usage", "vram", (int)Math.Round(vramPct),
                            "VRAM " + (int)Math.Round(vramPct) + "% " + vramUsed.ToString("F1") + "/" + (vramTotal / 1073741824.0).ToString("F1") + " GB",
                            (int)Math.Round(vramPct) + "%",
                            vramUsed.ToString("F1") + " / " + (vramTotal / 1073741824.0).ToString("F1") + " GB", false));
                    else
                        list.Add(Make("sys:vram", "VRAM usage", "vram", 0, "VRAM " + vramUsed.ToString("F1") + " GB", vramUsed.ToString("F1") + " GB", "total unknown", true));
                    log.AppendLine("  vram: " + vramUsed.ToString("F2") + " GB used");
                }
                double t;
                if (SsdTemp(out t))
                {
                    list.Add(Make("sys:ssd", "SSD temperature", "ssd", (int)Math.Round(t), "SSD " + (int)Math.Round(t) + "°", (int)Math.Round(t) + "°", TempNote(t, 65, 75), true));
                    log.AppendLine("  ssd temp: " + t.ToString("F0") + "C");
                }
                else log.AppendLine("  ssd temp: unavailable (" + ssdTempWhy + ")");
                log.AppendLine("  drive: " + DiskModel());
                double disk = DiskBusy();
                if (disk >= 0)
                {
                    list.Add(Make("sys:disk", "Storage utilization", "disk", (int)Math.Round(disk), "Disk " + (int)Math.Round(disk) + "%", (int)Math.Round(disk) + "%", null, false));
                    log.AppendLine("  disk busy: " + disk.ToString("F1") + "%");
                }
                double w;
                if (GpuPower(out w))
                {
                    list.Add(Make("sys:gpupower", "GPU power", "gpupower", 0, "GPU " + (int)Math.Round(w) + "W", (int)Math.Round(w) + "W", null, true));
                    log.AppendLine("  gpu power: " + w.ToString("F0") + "W (" + gpuPowerSrc + ")");
                }
                uint mhz;
                if (GpuClock(0, out mhz))
                {
                    list.Add(Make("sys:gpuclk", "GPU clock", "gpuclk", 0, "GPU " + mhz + "MHz", mhz + "MHz", null, true));
                    log.AppendLine("  gpu clock: " + mhz + " MHz");
                }
                if (GpuClock(2, out mhz))
                {
                    list.Add(Make("sys:memclk", "VRAM clock", "memclk", 0, "VRAM " + mhz + "MHz", mhz + "MHz", null, true));
                    log.AppendLine("  vram clock: " + mhz + " MHz");
                }
                // Network throughput, last in the cluster: down/up bytes/s
                // summed over every network interface except loopback.
                double down, up;
                if (NetSpeed(out down, out up))
                {
                    list.Add(Make("sys:net", "Network", "net", 0,
                        "NET ↓" + FmtSpeed(down) + " ↑" + FmtSpeed(up),
                        "↓" + FmtSpeed(down) + " ↑" + FmtSpeed(up),
                        "Down " + FmtSpeedFull(down) + " · Up " + FmtSpeedFull(up), true));
                    log.AppendLine("  net: down " + FmtSpeedFull(down) + ", up " + FmtSpeedFull(up));
                }
                Log = log.ToString();
                return list;
            }
        }

        static Reading Make(string key, string name, string kind, int pct, string text, string value, string detail, bool barless)
        {
            return new Reading { Key = key, Name = name, Kind = kind, Source = "System", Pct = pct, Text = text, Value = value, Detail = detail, Barless = barless };
        }

        // TraySense seg_temp thresholds: CPU/GPU 80C orange, 90C red; SSD 65C / 75C.
        static string TempNote(double t, double warm, double hot)
        {
            if (t >= hot) return "Very hot";
            if (t >= warm) return "Hot";
            return null;
        }

        // ---------------- Network throughput ----------------
        // PDH \Network Interface(*)\Bytes Received/Sent/sec, summed over
        // every interface except loopback. First sample is always 0, so the
        // first poll only primes the counters and reports nothing.
        bool NetSpeed(out double down, out double up)
        {
            down = up = 0;
            try
            {
                if (!netInit)
                {
                    netInit = true;
                    foreach (string inst in new PerformanceCounterCategory("Network Interface").GetInstanceNames())
                    {
                        if (inst.IndexOf("Loopback", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        try
                        {
                            netRecv.Add(new PerformanceCounter("Network Interface", "Bytes Received/sec", inst, true));
                            netSent.Add(new PerformanceCounter("Network Interface", "Bytes Sent/sec", inst, true));
                        }
                        catch { }
                    }
                    if (netRecv.Count == 0) return false;
                    foreach (var c in netRecv) c.NextValue();
                    foreach (var c in netSent) c.NextValue();
                    return false;
                }
                foreach (var c in netRecv) down += c.NextValue();
                foreach (var c in netSent) up += c.NextValue();
                return netRecv.Count > 0;
            }
            catch { return false; }
        }

        // Compact speed for the strip: "1.2M", "320K", "800".
        static string FmtSpeed(double b)
        {
            if (b >= 1048576) return (b / 1048576).ToString("F1") + "M";
            if (b >= 1024) return (b / 1024).ToString("F0") + "K";
            return ((int)b).ToString();
        }

        // Full speed for the panel sub-line: "1.2 MB/s", "320 KB/s", "800 B/s".
        static string FmtSpeedFull(double b)
        {
            if (b >= 1048576) return (b / 1048576).ToString("F1") + " MB/s";
            if (b >= 1024) return (b / 1024).ToString("F0") + " KB/s";
            return ((int)b) + " B/s";
        }

        // ---------------- CPU % ----------------
        double CpuPct()
        {
            try
            {
                SysNative.FILETIME idle, kernel, user;
                if (!SysNative.GetSystemTimes(out idle, out kernel, out user)) return -1;
                ulong id = ((ulong)idle.High << 32) | idle.Low;
                ulong tot = (((ulong)kernel.High << 32) | kernel.Low) + (((ulong)user.High << 32) | user.Low);
                double v = -1;
                if (havePrevCpu && tot > prevTotal)
                    v = 100.0 * (1.0 - (double)(id - prevIdle) / (double)(tot - prevTotal));
                prevIdle = id; prevTotal = tot; havePrevCpu = true;
                return v < 0 ? -1 : (v > 100 ? 100 : v);
            }
            catch { return -1; }
        }

        // ---------------- RAM % ----------------
        bool RamPct(out double pct, out double usedGb, out double totalGb)
        {
            pct = 0; usedGb = 0; totalGb = 0;
            try
            {
                var m = new SysNative.MEMORYSTATUSEX();
                m.Length = (uint)Marshal.SizeOf(typeof(SysNative.MEMORYSTATUSEX));
                if (!SysNative.GlobalMemoryStatusEx(ref m)) return false;
                totalGb = m.TotalPhys / 1073741824.0;
                usedGb = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
                pct = 100.0 * (m.TotalPhys - m.AvailPhys) / (double)m.TotalPhys;
                return true;
            }
            catch { return false; }
        }

        // ---------------- PDH counters ----------------
        void RefreshCounters()
        {
            if (DateTime.UtcNow - counterRefresh < TimeSpan.FromSeconds(30)) return;
            counterRefresh = DateTime.UtcNow;
            try
            {
                Rebuild(gpuCounters, "GPU Engine", "Utilization Percentage");
                Rebuild(vramCounters, "GPU Adapter Memory", "Dedicated Usage");
                if (thermCounters.Count == 0)
                    try
                    {
                        foreach (string inst in new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames())
                            try { thermCounters.Add(new PerformanceCounter("Thermal Zone Information", "Temperature", inst, true)); } catch { }
                    }
                    catch { }
                if (diskCounter == null)
                    try
                    {
                        string drive = "C";
                        try { drive = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1); } catch { }
                        foreach (string inst in new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames())
                        {
                            if (inst.IndexOf(" " + drive + ":", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                try { diskCounter = new PerformanceCounter("PhysicalDisk", "% Idle Time", inst, true); diskInstance = inst; } catch { }
                                break;
                            }
                        }
                    }
                    catch { }
            }
            catch { }
        }

        static void Rebuild(Dictionary<string, PerformanceCounter> map, string category, string counter)
        {
            try
            {
                var names = new PerformanceCounterCategory(category).GetInstanceNames();
                var keep = new HashSet<string>(names);
                var dead = new List<string>();
                foreach (string k in map.Keys) if (!keep.Contains(k)) dead.Add(k);
                foreach (string k in dead) { try { map[k].Dispose(); } catch { } map.Remove(k); }
                foreach (string n in names)
                    if (!map.ContainsKey(n))
                        try { map[n] = new PerformanceCounter(category, counter, n, true); } catch { }
            }
            catch { }
        }

        static string LuidOf(string instance)
        {
            int i = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (i < 0 || i + 26 > instance.Length) return null;
            return instance.Substring(i, 26).ToLowerInvariant();
        }

        double GpuPct()
        {
            try
            {
                RefreshCounters();
                if (gpuCounters.Count == 0) return -1;
                var byLuid = new Dictionary<string, Dictionary<int, double>>();
                foreach (var kv in gpuCounters)
                {
                    int e = kv.Key.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase);
                    if (e < 0) continue;
                    int eng;
                    if (!int.TryParse(kv.Key.Substring(e + 5).Split('_')[0], out eng)) continue;
                    float v;
                    try { v = kv.Value.NextValue(); } catch { continue; }
                    string luid = LuidOf(kv.Key) ?? "";
                    Dictionary<int, double> d;
                    if (!byLuid.TryGetValue(luid, out d)) byLuid[luid] = d = new Dictionary<int, double>();
                    double cur;
                    d.TryGetValue(eng, out cur);
                    d[eng] = cur + v;
                }
                double best = -1;
                string bestLuid = null;
                foreach (var kv in byLuid)
                    foreach (double v in kv.Value.Values)
                        if (v > best) { best = v; bestLuid = kv.Key; }
                if (best < 0) return -1;
                if (bestLuid != null && bestLuid.Length > 0) chosenLuid = bestLuid;
                return best > 100 ? 100 : best;
            }
            catch { return -1; }
        }

        bool Vram(out double usedGb, out double pct)
        {
            usedGb = 0; pct = -1;
            try
            {
                RefreshCounters();
                if (vramCounters.Count == 0) return false;
                EnsureWmi();
                var byLuid = new Dictionary<string, double>();
                foreach (var kv in vramCounters)
                {
                    float v;
                    try { v = kv.Value.NextValue(); } catch { continue; }
                    string luid = LuidOf(kv.Key) ?? "";
                    double cur;
                    byLuid.TryGetValue(luid, out cur);
                    byLuid[luid] = cur + v;
                }
                double used = 0;
                bool found = false;
                if (chosenLuid != null)
                {
                    double u;
                    if (byLuid.TryGetValue(chosenLuid, out u)) { used = u; found = true; }
                }
                if (!found)
                    foreach (double u in byLuid.Values) if (u > used) used = u;
                if (used <= 0) return false;
                usedGb = used / 1073741824.0;
                // AdapterRAM is a 32-bit WMI value: 0xFFFFFFFF means "over 4 GB, unknown".
                if (vramTotal > 0 && vramTotal != 0xFFFFFFFF && vramTotal < 68719476736UL)
                    pct = 100.0 * used / (double)vramTotal;
                return true;
            }
            catch { return false; }
        }

        double DiskBusy()
        {
            try
            {
                RefreshCounters();
                if (diskCounter == null) return -1;
                double idle = diskCounter.NextValue();
                double busy = 100.0 - idle;
                return busy < 0 ? 0 : busy > 100 ? 100 : busy;
            }
            catch { return -1; }
        }

        // ---------------- WMI: GPU name / VRAM total / CPU vendor ----------------
        void EnsureWmi()
        {
            if (wmiTried) return;
            wmiTried = true;
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT AdapterRAM, Name FROM Win32_VideoController"))
                foreach (ManagementObject o in s.Get())
                {
                    try
                    {
                        object ram = o["AdapterRAM"];
                        if (ram != null)
                        {
                            ulong r = Convert.ToUInt64(ram);
                            if (r > vramTotal) { vramTotal = r; gpuName = o["Name"] as string; }
                        }
                    }
                    catch { }
                    try { o.Dispose(); } catch { }
                }
            }
            catch { }
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_Processor"))
                foreach (ManagementObject o in s.Get())
                {
                    cpuVendor = o["Manufacturer"] as string;
                    try { o.Dispose(); } catch { }
                    break;
                }
            }
            catch { }
        }

        string GpuName()
        {
            EnsureWmi();
            return gpuName;
        }

        // ---------------- SSD temp ----------------
        bool SsdTemp(out double temp)
        {
            temp = 0;
            try
            {
                if (!diskHandleTried)
                {
                    diskHandleTried = true;
                    string drive = "C";
                    try { drive = Path.GetPathRoot(Environment.SystemDirectory).Substring(0, 1); } catch { }
                    IntPtr vol = SysNative.CreateFile(@"\\.\" + drive + ":", 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (vol != IntPtr.Zero && vol != new IntPtr(-1))
                    {
                        try
                        {
                            byte[] buf = new byte[32];
                            uint got;
                            GCHandle hb = GCHandle.Alloc(buf, GCHandleType.Pinned);
                            try
                            {
                                if (SysNative.DeviceIoControl(vol, 0x00560000, IntPtr.Zero, 0, hb.AddrOfPinnedObject(), (uint)buf.Length, out got, IntPtr.Zero) && got >= 8)
                                {
                                    uint num = BitConverter.ToUInt32(buf, 4);
                                    diskNumber = (int)num;
                                    diskHandle = SysNative.CreateFile(@"\\.\PhysicalDrive" + num, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                                }
                            }
                            finally { hb.Free(); }
                        }
                        finally { SysNative.CloseHandle(vol); }
                    }
                }
                if (diskHandle == IntPtr.Zero || diskHandle == new IntPtr(-1)) { ssdTempWhy = "no drive handle"; return false; }
                byte[] q = new byte[8];
                Array.Copy(BitConverter.GetBytes(6u), 0, q, 0, 4); // StorageDeviceTemperatureProperty
                Array.Copy(BitConverter.GetBytes(0u), 0, q, 4, 4); // PropertyStandardQuery
                byte[] b = new byte[512];
                uint outLen;
                GCHandle h = GCHandle.Alloc(b, GCHandleType.Pinned);
                try
                {
                    IntPtr inPtr = Marshal.AllocHGlobal(8);
                    try
                    {
                        Marshal.Copy(q, 0, inPtr, 8);
                        if (!SysNative.DeviceIoControl(diskHandle, 0x002D1400, inPtr, 8, h.AddrOfPinnedObject(), (uint)b.Length, out outLen, IntPtr.Zero)) { ssdTempWhy = "property refused"; return SsdTempFallback(out temp); }
                    }
                    finally { Marshal.FreeHGlobal(inPtr); }
                }
                finally { h.Free(); }
                if (outLen < 12) { ssdTempWhy = "property reply too short (outLen=" + outLen + ")"; return SsdTempFallback(out temp); }
                int count = BitConverter.ToUInt16(b, 10);
                int entrySize = Marshal.SizeOf(typeof(SysNative.TempInfo));
                int mx = -1000;
                for (int i = 0; i < count; i++)
                {
                    int off = 12 + i * entrySize;
                    if (off + entrySize > outLen) break;
                    short t = BitConverter.ToInt16(b, off + 2);
                    if (t > mx) mx = t;
                }
                if (mx > 0 && mx < 150) { temp = mx; ssdTempWhy = ""; return true; }
                ssdTempWhy = count == 0 ? "property: no sensors" : "property: temp out of range";
                return SsdTempFallback(out temp);
            }
            catch { ssdTempWhy = "property exception"; return SsdTempFallback(out temp); }
        }

        // NVMe log, then classic ATA SMART, then WMI SMART: whichever
        // answers. The dump line lists every path's reason, so a failure
        // says exactly which doors are closed.
        bool SsdTempFallback(out double temp)
        {
            string why = ssdTempWhy; // property-path reason (may be "")
            if (NvmeSsdTemp(out temp)) return true;
            if (why.Length > 0) why += "; ";
            why += "NVMe: " + ssdTempWhy;
            if (AtaSsdTemp(out temp)) return true;
            why += "; ATA: " + ssdTempWhy;
            if (WmiSsdTemp(out temp)) return true;
            ssdTempWhy = why + "; WMI: " + ssdTempWhy;
            return false;
        }

        // WMI SMART (root\WMI, MSStorageDriver_ATAPISmartData): a different
        // channel from the IOCTLs above — some drivers that refuse direct
        // queries still publish SMART data here. A dead WMI path is
        // remembered after the first attempt, so it costs nothing per poll.
        bool WmiSsdTemp(out double temp)
        {
            temp = 0;
            if (wmiSmartTried && !wmiSmartOk) { ssdTempWhy = wmiSmartWhy; return false; }
            wmiSmartTried = true;
            wmiSmartWhy = "WMI SMART: no temp attribute";
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT VendorSpecific FROM MSStorageDriver_ATAPISmartData"))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        byte[] data = obj["VendorSpecific"] as byte[];
                        if (data == null || data.Length < 2 + 12) continue;
                        for (int i = 0; i < 30; i++)
                        {
                            int off = 2 + i * 12;
                            if (off + 12 > data.Length) break;
                            byte id = data[off];
                            if (id == 0) break;
                            if (id == 0xC2 || id == 0xBE)
                            {
                                wmiSmartOk = true;
                                int t = data[off + 5];
                                if (t > 0 && t < 120) { temp = t; ssdTempWhy = ""; return true; }
                                wmiSmartWhy = "WMI SMART: temp out of range";
                                ssdTempWhy = wmiSmartWhy;
                                return false;
                            }
                        }
                    }
                }
                ssdTempWhy = wmiSmartWhy;
                return false;
            }
            catch (Exception e)
            {
                wmiSmartWhy = "WMI SMART failed (" + e.GetType().Name + ")";
                ssdTempWhy = wmiSmartWhy;
                return false;
            }
        }

        // Classic ATA SMART (SMART_RCV_DRIVE_DATA): the fallback for SATA
        // SSDs, which answer neither the temperature property nor the NVMe
        // log. Reads attribute 0xC2 (temperature), else 0xBE.
        bool AtaSsdTemp(out double temp)
        {
            temp = 0;
            ssdTempWhy = "ATA SMART refused";
            try
            {
                if (diskHandle == IntPtr.Zero || diskHandle == new IntPtr(-1) || diskNumber < 0) { ssdTempWhy = "no drive handle"; return false; }
                // SENDCMDINPARAMS: cBufferSize + IDEREGS + drive number.
                byte[] q = new byte[32];
                Array.Copy(BitConverter.GetBytes(512u), 0, q, 0, 4);
                q[4] = 0xD0; // SMART READ DATA
                q[5] = 1;
                q[6] = 1;
                q[7] = 0x4F;
                q[8] = 0xC2;
                q[9] = (byte)(0xA0 | (diskNumber << 4));
                q[10] = 0xB0; // SMART
                q[12] = (byte)diskNumber;
                // SENDCMDOUTPARAMS (16) + 512 bytes of SMART data.
                byte[] b = new byte[16 + 512];
                uint outLen;
                GCHandle h = GCHandle.Alloc(b, GCHandleType.Pinned);
                try
                {
                    IntPtr inPtr = Marshal.AllocHGlobal(32);
                    try
                    {
                        Marshal.Copy(q, 0, inPtr, 32);
                        if (!SysNative.DeviceIoControl(diskHandle, 0x0007C088, inPtr, 32, h.AddrOfPinnedObject(), (uint)b.Length, out outLen, IntPtr.Zero)) return false;
                    }
                    finally { Marshal.FreeHGlobal(inPtr); }
                }
                finally { h.Free(); }
                if (outLen < 16 + 512) { ssdTempWhy = "ATA SMART reply too short (outLen=" + outLen + ")"; return false; }
                if (b[4] != 0) { ssdTempWhy = "ATA SMART driver error"; return false; }
                // SMART data: 2-byte revision, then 12-byte attributes.
                for (int i = 0; i < 30; i++)
                {
                    int off = 16 + 2 + i * 12;
                    byte id = b[off];
                    if (id == 0) break;
                    if (id == 0xC2 || id == 0xBE)
                    {
                        int t = b[off + 5];
                        if (t > 0 && t < 120) { temp = t; ssdTempWhy = ""; return true; }
                    }
                }
                ssdTempWhy = "ATA SMART: no temp attribute";
                return false;
            }
            catch { ssdTempWhy = "exception"; return false; }
        }

        // Drive model for --dump (cached): StorageDeviceProperty on the
        // same physical-drive handle. Tells us NVMe vs SATA when the
        // temperature paths disagree with the drive.
        string DiskModel()
        {
            if (diskModelTried) return diskModel ?? "unknown";
            diskModelTried = true;
            diskModel = "unknown";
            try
            {
                if (diskHandle == IntPtr.Zero || diskHandle == new IntPtr(-1)) return diskModel;
                byte[] q = new byte[8];
                Array.Copy(BitConverter.GetBytes(0u), 0, q, 0, 4); // StorageDeviceProperty
                Array.Copy(BitConverter.GetBytes(0u), 0, q, 4, 4); // PropertyStandardQuery
                byte[] b = new byte[1024];
                uint outLen;
                GCHandle h = GCHandle.Alloc(b, GCHandleType.Pinned);
                try
                {
                    IntPtr inPtr = Marshal.AllocHGlobal(8);
                    try
                    {
                        Marshal.Copy(q, 0, inPtr, 8);
                        if (!SysNative.DeviceIoControl(diskHandle, 0x002D1400, inPtr, 8, h.AddrOfPinnedObject(), (uint)b.Length, out outLen, IntPtr.Zero)) { diskModel = "unknown (query refused)"; return diskModel; }
                    }
                    finally { Marshal.FreeHGlobal(inPtr); }
                }
                finally { h.Free(); }
                if (outLen < 36) { diskModel = "unknown (short reply outLen=" + outLen + ")"; return diskModel; }
                int vOff = BitConverter.ToInt32(b, 12);
                int pOff = BitConverter.ToInt32(b, 16);
                string model = (ReadAscii(b, vOff) + " " + ReadAscii(b, pOff)).Trim();
                if (model.Length > 0) diskModel = model;
                return diskModel;
            }
            catch { return diskModel ?? "unknown"; }
        }

        static string ReadAscii(byte[] b, int off)
        {
            if (off <= 0 || off >= b.Length) return "";
            int end = off;
            while (end < b.Length && b[end] != 0) end++;
            try { return Encoding.ASCII.GetString(b, off, end - off).Trim(); } catch { return ""; }
        }

        // NVMe SMART log (log page 2) via IOCTL_STORAGE_QUERY_PROPERTY:
        // the temperature property above stays empty on many NVMe drives
        // (notably in mini PCs), while the drive always answers its SMART
        // log. Composite temperature is log bytes 1-2, Kelvin.
        bool NvmeSsdTemp(out double temp)
        {
            temp = 0;
            ssdTempWhy = "no NVMe SMART temp";
            try
            {
                if (diskHandle == IntPtr.Zero || diskHandle == new IntPtr(-1)) { ssdTempWhy = "no drive handle"; return false; }
                // STORAGE_PROPERTY_QUERY (8) + STORAGE_PROTOCOL_SPECIFIC_DATA (40).
                byte[] q = new byte[48];
                Array.Copy(BitConverter.GetBytes(7u), 0, q, 0, 4);    // StorageDeviceProtocolSpecificProperty
                Array.Copy(BitConverter.GetBytes(0u), 0, q, 4, 4);    // PropertyStandardQuery
                Array.Copy(BitConverter.GetBytes(1u), 0, q, 8, 4);    // ProtocolTypeNvme
                Array.Copy(BitConverter.GetBytes(2u), 0, q, 12, 4);   // NVMeDataTypeLogPage
                Array.Copy(BitConverter.GetBytes(0x02u), 0, q, 16, 4); // SMART/Health log page
                Array.Copy(BitConverter.GetBytes(0u), 0, q, 20, 4);   // SubValue
                Array.Copy(BitConverter.GetBytes(40u), 0, q, 24, 4);  // ProtocolDataOffset
                Array.Copy(BitConverter.GetBytes(512u), 0, q, 28, 4); // ProtocolDataLength
                byte[] b = new byte[8 + 40 + 512];
                uint outLen;
                // The driver needs Version/Size filled in up front:
                // without them it answers the descriptor but returns no
                // log data ("NVMe log too short" in --dump).
                Array.Copy(BitConverter.GetBytes(48u), 0, b, 0, 4);  // Version
                Array.Copy(BitConverter.GetBytes((uint)b.Length), 0, b, 4, 4); // Size
                GCHandle h = GCHandle.Alloc(b, GCHandleType.Pinned);
                try
                {
                    IntPtr inPtr = Marshal.AllocHGlobal(48);
                    try
                    {
                        Marshal.Copy(q, 0, inPtr, 48);
                        if (!SysNative.DeviceIoControl(diskHandle, 0x002D1400, inPtr, 48, h.AddrOfPinnedObject(), (uint)b.Length, out outLen, IntPtr.Zero)) { ssdTempWhy = "NVMe query refused"; return false; }
                    }
                    finally { Marshal.FreeHGlobal(inPtr); }
                }
                finally { h.Free(); }
                if (outLen < 8 + 40 + 3) { ssdTempWhy = "NVMe reply too short (outLen=" + outLen + ")"; return false; }
                // Descriptor: Version + Size (8), then ProtocolSpecificData;
                // ProtocolDataOffset is relative to that struct.
                int baseOff = 8 + (int)BitConverter.ToUInt32(b, 24);
                uint dataLen = BitConverter.ToUInt32(b, 28);
                if (dataLen < 3 || baseOff + 3 > outLen) { ssdTempWhy = "NVMe log too short (outLen=" + outLen + " dataLen=" + dataLen + " off=" + baseOff + ")"; return false; }
                ushort kelvin = BitConverter.ToUInt16(b, baseOff + 1);
                double c = kelvin - 273.15;
                if (c > 0 && c < 120) { temp = c; ssdTempWhy = ""; return true; }
                ssdTempWhy = "NVMe temp out of range";
                return false;
            }
            catch { ssdTempWhy = "exception"; return false; }
        }

        // ---------------- CPU temp: PawnIO, else Thermal Zone ----------------
        bool CpuTemp(out double temp)
        {
            temp = 0;
            try
            {
                double t;
                if (PioTemp(out t)) { temp = t; return true; }
                RefreshCounters();
                double mx = 0;
                foreach (var c in thermCounters)
                {
                    try
                    {
                        float v = c.NextValue();
                        if (v > mx) mx = v;
                    }
                    catch { }
                }
                if (mx > 200) { temp = mx - 273.15; return true; }
                return false;
            }
            catch { return false; }
        }

        bool PioTemp(out double temp)
        {
            temp = 0;
            if (!PioInit()) return false;
            try
            {
                ulong v;
                if (pioVendor == 1)
                {
                    if (!PioExec("ioctl_read_msr", 0x1B1, out v)) return false;
                    temp = tjMax - (double)((v >> 16) & 0x7F);
                }
                else
                {
                    bool locked = false;
                    try
                    {
                        if (pciMutex != null) locked = pciMutex.WaitOne(20);
                        if (!PioExec("ioctl_read_smn", 0x00059800, out v)) return false;
                        double c = ((v >> 21) & 0x7FF) / 8.0;
                        if ((v & 0x80000) != 0) c -= 49.0;
                        temp = c;
                    }
                    finally { if (locked && pciMutex != null) pciMutex.ReleaseMutex(); }
                }
                return temp > 0 && temp < 125;
            }
            catch { return false; }
        }

        // ---------------- CPU power (RAPL) ----------------
        // Same as TraySense's read_cpu_power: the package energy MSR via
        // PawnIO (needs the driver, like CPU temperature). Intel: unit
        // MSR 0x606, energy MSR 0x611; AMD: 0xC0010299 / 0xC001029B.
        // Power is the energy delta over time; the first sample only
        // primes the counter, so the reading appears from the 2nd poll.
        bool CpuPower(out double watts)
        {
            watts = 0;
            if (!PioInit()) return false;
            try
            {
                ulong v;
                if (cpuPowerUnit == 0)
                {
                    if (!PioExec("ioctl_read_msr", pioVendor == 1 ? 0x606 : 0xC0010299, out v)) return false;
                    cpuPowerUnit = 1.0 / (double)(1UL << (int)((v >> 8) & 0x1F));
                    if (cpuPowerUnit <= 0) return false;
                }
                if (!PioExec("ioctl_read_msr", pioVendor == 1 ? 0x611 : 0xC001029B, out v)) return false;
                ulong e = v & 0xFFFFFFFFUL;
                DateTime now = DateTime.UtcNow;
                if (lastCpuPowerAt != DateTime.MinValue && now > lastCpuPowerAt)
                {
                    double dt = (now - lastCpuPowerAt).TotalSeconds;
                    double w = (double)((e - lastCpuEnergy) & 0xFFFFFFFFUL) * cpuPowerUnit / dt;
                    lastCpuEnergy = e; lastCpuPowerAt = now;
                    if (w >= 0 && w < 1000) { watts = w; return true; }
                    return false;
                }
                lastCpuEnergy = e; lastCpuPowerAt = now;
                return false;
            }
            catch { return false; }
        }

        bool PioInit()
        {
            if (pioState != 0) return pioState == 1;
            pioState = 3;
            try
            {
                EnsureWmi();
                string m = (cpuVendor ?? "").ToLowerInvariant();
                if (m.IndexOf("intel") >= 0) pioVendor = 1;
                else if (m.IndexOf("amd") >= 0) pioVendor = 2;
                else return false;
                pioState = 4;
                pioHandle = SysNative.CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (pioHandle == IntPtr.Zero || pioHandle == new IntPtr(-1)) { pioState = 2; return false; }
                string res = pioVendor == 1 ? "IntelMSR.bin" : "AMDFamily17.bin";
                byte[] bin;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(res))
                {
                    if (s == null) return false;
                    bin = new byte[s.Length];
                    if (s.Read(bin, 0, bin.Length) != bin.Length) return false;
                }
                uint got;
                GCHandle h = GCHandle.Alloc(bin, GCHandleType.Pinned);
                try
                {
                    if (!SysNative.DeviceIoControl(pioHandle, IoctlPioLoad, h.AddrOfPinnedObject(), (uint)bin.Length, IntPtr.Zero, 0, out got, IntPtr.Zero)) return false;
                }
                finally { h.Free(); }
                if (pioVendor == 1)
                {
                    ulong v;
                    if (PioExec("ioctl_read_msr", 0x1A2, out v))
                    {
                        int tj = (int)((v >> 16) & 0xFF);
                        if (tj >= 60 && tj <= 130) tjMax = tj;
                    }
                }
                else
                {
                    try { pciMutex = new Mutex(false, @"Global\Access_PCI"); } catch { pciMutex = null; }
                }
                pioState = 1;
                return true;
            }
            catch { return false; }
        }

        bool PioExec(string fn, ulong input, out ulong output)
        {
            output = 0;
            try
            {
                byte[] buf = new byte[40];
                byte[] name = Encoding.ASCII.GetBytes(fn);
                Array.Copy(name, 0, buf, 0, Math.Min(name.Length, 31));
                Array.Copy(BitConverter.GetBytes(input), 0, buf, 32, 8);
                byte[] outBuf = new byte[8];
                uint got;
                GCHandle hi = GCHandle.Alloc(buf, GCHandleType.Pinned);
                GCHandle ho = GCHandle.Alloc(outBuf, GCHandleType.Pinned);
                try
                {
                    if (!SysNative.DeviceIoControl(pioHandle, IoctlPioExec, hi.AddrOfPinnedObject(), 40, ho.AddrOfPinnedObject(), 8, out got, IntPtr.Zero)) return false;
                }
                finally { hi.Free(); ho.Free(); }
                if (got < 8) return false;
                output = BitConverter.ToUInt64(outBuf, 0);
                return true;
            }
            catch { return false; }
        }

        // ---------------- GPU temp / power / clocks (NVML) ----------------
        bool NvmlInit()
        {
            if (nvmlTried) return nvmlDev != IntPtr.Zero;
            nvmlTried = true;
            try
            {
                IntPtr lib = SysNative.LoadLibrary("nvml.dll");
                if (lib == IntPtr.Zero) lib = SysNative.LoadLibrary(@"C:\Program Files\NVIDIA Corporation\NVSMI\nvml.dll");
                if (lib == IntPtr.Zero) return false;
                nvmlInit = GetProc<NvmlInitFn>(lib, "nvmlInit_v2");
                nvmlHandle = GetProc<NvmlHandleFn>(lib, "nvmlDeviceGetHandleByIndex_v2");
                nvmlTemp = GetProc<NvmlTempFn>(lib, "nvmlDeviceGetTemperature");
                nvmlPower = GetProc<NvmlPowerFn>(lib, "nvmlDeviceGetPowerUsage");
                nvmlClock = GetProc<NvmlClockFn>(lib, "nvmlDeviceGetClockInfo");
                if (nvmlInit == null || nvmlHandle == null) return false;
                if (nvmlInit() != 0) return false;
                IntPtr dev = IntPtr.Zero;
                if (nvmlHandle(0, ref dev) != 0) return false;
                nvmlDev = dev;
                return true;
            }
            catch { return false; }
        }

        static T GetProc<T>(IntPtr lib, string name) where T : class
        {
            try
            {
                IntPtr p = SysNative.GetProcAddress(lib, name);
                if (p == IntPtr.Zero) return null;
                return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
            }
            catch { return null; }
        }

        bool GpuTemp(out double temp)
        {
            temp = 0;
            try
            {
                // TraySense's read_gpu_temp order: D3DKMT first (AMD, Intel,
                // NVIDIA on Windows 10+), then NVML (NVIDIA only).
                if (KmtTemp(out temp)) { gpuTempWhy = ""; return true; }
                if (NvmlInit() && nvmlTemp != null)
                {
                    uint t = 0;
                    if (nvmlTemp(nvmlDev, 0, ref t) == 0 && t != 0) { temp = t; gpuTempWhy = ""; return true; }
                }
                gpuTempWhy = (kmtOk ? "D3DKMT: no sensor" : "D3DKMT: no adapter") + "; NVML: " + (nvmlDev == IntPtr.Zero ? "unavailable" : "no reading");
                return false;
            }
            catch { gpuTempWhy = "exception"; return false; }
        }

        bool GpuPower(out double watts)
        {
            watts = 0;
            try
            {
                if (NvmlInit() && nvmlPower != null)
                {
                    uint mw = 0;
                    if (nvmlPower(nvmlDev, ref mw) == 0)
                    {
                        watts = mw / 1000.0;
                        if (watts > 0 && watts < 1000) { gpuPowerSrc = "NVML"; return true; }
                    }
                }
                // AMD: ADL PMLog board/ASIC power (watts), before D3DKMT.
                double aw;
                if (AdlPower(out aw)) { watts = aw; gpuPowerSrc = "ADL"; return true; }
                // Last resort, like TraySense: D3DKMT Power is deciwatts.
                SysNative.KmtPerf p;
                if (KmtPerf(out p) && p.Power > 0 && p.Power <= 2000)
                {
                    watts = p.Power / 10.0;
                    if (watts > 0 && watts < 1000) { gpuPowerSrc = "D3DKMT"; return true; }
                    return false;
                }
                return false;
            }
            catch { return false; }
        }

        bool GpuClock(int type, out uint mhz)
        {
            mhz = 0;
            try
            {
                if (NvmlInit() && nvmlClock != null)
                {
                    uint c = 0;
                    if (nvmlClock(nvmlDev, type, ref c) == 0 && c != 0) { mhz = c; return true; }
                }
                // AMD: ADL PMLog sensor 1 = GPU clock, 2 = VRAM clock.
                // NVML clock types are 0 = graphics, 2 = memory.
                if (AdlClock(type == 0 ? 1 : 2, out mhz)) return true;
                return false;
            }
            catch { return false; }
        }

        // ---------------- AMD GPU power / clocks (ADL PMLog) ----------------
        // Same as TraySense's init_adl/adl_query/adl_power: ADL2 PMLog
        // sensors 73 = board power, 23 = ASIC power (watts), 1 = GPU
        // clock, 2 = VRAM clock (MHz). This is the source that actually
        // moves on AMD APUs — D3DKMT's Power field returns a fixed
        // placeholder there, which is why GPU power looked stuck at 2W.
        static IntPtr AdlAlloc(int size)
        {
            try { return Marshal.AllocHGlobal(size > 0 ? size : 1); }
            catch { return IntPtr.Zero; }
        }

        bool AdlInit()
        {
            if (adlTried) return adlCtx != IntPtr.Zero && adlIndex >= 0;
            adlTried = true;
            try
            {
                IntPtr lib = SysNative.LoadLibrary("atiadlxx.dll");
                if (lib == IntPtr.Zero) lib = SysNative.LoadLibrary("atiadlxy.dll");
                if (lib == IntPtr.Zero) return false;
                adlCreate = GetProc<AdlCreateFn>(lib, "ADL2_Main_Control_Create");
                adlCount = GetProc<AdlCountFn>(lib, "ADL2_Adapter_NumberOfAdapters_Get");
                adlPmLog = GetProc<AdlPmLogFn>(lib, "ADL2_New_QueryPMLogData_Get");
                if (adlCreate == null || adlCount == null || adlPmLog == null) return false;
                adlMalloc = new AdlMallocFn(AdlAlloc);
                IntPtr ctx = IntPtr.Zero;
                if (adlCreate(adlMalloc, 1, out ctx) != 0 || ctx == IntPtr.Zero) return false;
                adlCtx = ctx;
                int n = 0;
                if (adlCount(adlCtx, ref n) != 0 || n <= 0) return false;
                // First adapter that reports a power sensor (supported
                // flag, value may still be 0 at idle), like TraySense.
                for (int i = 0; i < n && i < 64; i++)
                {
                    if (AdlHasPowerSensor(i)) { adlIndex = i; return true; }
                }
                return false;
            }
            catch { return false; }
        }

        bool AdlHasPowerSensor(int index)
        {
            if (!AdlQuery(index)) return false;
            bool sup;
            AdlSensor(73, out sup);
            if (sup) return true;
            AdlSensor(23, out sup);
            return sup;
        }

        bool AdlQuery(int index)
        {
            if (adlCtx == IntPtr.Zero || adlPmLog == null) return false;
            // Reuse this poll's query: power and both clocks read the
            // same adapter, so one driver call serves all three.
            if (adlFresh && adlCachedIndex == index) return true;
            try
            {
                Array.Clear(adlBuf, 0, adlBuf.Length);
                byte[] sz = BitConverter.GetBytes(2052);
                adlBuf[0] = sz[0]; adlBuf[1] = sz[1]; adlBuf[2] = sz[2]; adlBuf[3] = sz[3];
                GCHandle h = GCHandle.Alloc(adlBuf, GCHandleType.Pinned);
                try
                {
                    if (adlPmLog(adlCtx, index, h.AddrOfPinnedObject()) != 0) return false;
                    adlFresh = true;
                    adlCachedIndex = index;
                    return true;
                }
                finally { h.Free(); }
            }
            catch { return false; }
        }

        // Sensor i in the last query: supported flag at 4+i*8, value at 8+i*8.
        int AdlSensor(int id, out bool supported)
        {
            supported = BitConverter.ToInt32(adlBuf, 4 + id * 8) != 0;
            return BitConverter.ToInt32(adlBuf, 8 + id * 8);
        }

        bool AdlPowerAt(int index, out double watts)
        {
            watts = 0;
            if (!AdlQuery(index)) return false;
            bool sup;
            int v = AdlSensor(73, out sup);
            if (sup && v > 0) { watts = v; return watts > 0 && watts < 1000; }
            v = AdlSensor(23, out sup);
            if (sup && v > 0) { watts = v; return watts > 0 && watts < 1000; }
            return false;
        }

        bool AdlPower(out double watts)
        {
            watts = 0;
            if (!AdlInit()) return false;
            return AdlPowerAt(adlIndex, out watts);
        }

        bool AdlClock(int sensor, out uint mhz)
        {
            mhz = 0;
            try
            {
                if (!AdlInit()) return false;
                if (!AdlQuery(adlIndex)) return false;
                bool sup;
                int v = AdlSensor(sensor, out sup);
                if (!sup || v <= 0 || v > 10000) return false;
                mhz = (uint)v;
                return true;
            }
            catch { return false; }
        }

        // ---------------- GPU temp / power (D3DKMT) ----------------
        // Same as TraySense's kmt_perf/kmt_temp: open the adapter found by
        // the GPU usage counters (chosenLuid) and read ADAPTERPERFDATA
        // (type 62). This is what makes AMD (and Intel) GPU temperature
        // work — NVML alone only covers NVIDIA.
        bool KmtInit()
        {
            if (kmtTried) return kmtOk;
            kmtTried = true;
            try
            {
                if (chosenLuid == null || chosenLuid.Length != 26) return false;
                uint high, low;
                if (!uint.TryParse(chosenLuid.Substring(7, 8), System.Globalization.NumberStyles.HexNumber, null, out high)) return false;
                if (!uint.TryParse(chosenLuid.Substring(18, 8), System.Globalization.NumberStyles.HexNumber, null, out low)) return false;
                var o = new SysNative.KmtOpen();
                o.Luid = new SysNative.KmtLuid { LowPart = low, HighPart = (int)high };
                if (SysNative.D3DKMTOpenAdapterFromLuid(ref o) != 0) return false;
                kmtAdapter = o.Adapter;
                kmtOk = true;
                return true;
            }
            catch { return false; }
        }

        bool KmtPerf(out SysNative.KmtPerf p)
        {
            p = new SysNative.KmtPerf();
            if (!KmtInit()) return false;
            int size = Marshal.SizeOf(typeof(SysNative.KmtPerf));
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(p, buf, false);
                var q = new SysNative.KmtQai { Adapter = kmtAdapter, Type = 62, Data = buf, Size = (uint)size };
                if (SysNative.D3DKMTQueryAdapterInfo(ref q) != 0) return false;
                p = (SysNative.KmtPerf)Marshal.PtrToStructure(buf, typeof(SysNative.KmtPerf));
                return true;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        bool KmtTemp(out double temp)
        {
            temp = 0;
            try
            {
                SysNative.KmtPerf p;
                if (!KmtPerf(out p) || p.Temperature == 0) return false;
                temp = p.Temperature / 10.0;
                return temp > 0 && temp < 150;
            }
            catch { return false; }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int NvmlInitFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int NvmlHandleFn(uint index, ref IntPtr device);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int NvmlTempFn(IntPtr device, int sensorType, ref uint temp);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int NvmlPowerFn(IntPtr device, ref uint powerMw);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int NvmlClockFn(IntPtr device, int clockType, ref uint clockMHz);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate IntPtr AdlMallocFn(int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int AdlCreateFn(AdlMallocFn alloc, int enumConnected, out IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int AdlCountFn(IntPtr context, ref int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int AdlPmLogFn(IntPtr context, int adapterIndex, IntPtr log);

    static class SysNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct FILETIME { public uint Low; public uint High; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TempInfo
        {
            public ushort Index;
            public short Temperature;
            public ushort OverThreshold;
            public ushort CriticalThreshold;
            public ushort OverThreshold2;
            public ushort CriticalThreshold2;
            public byte Reserved;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buf);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inLen, IntPtr outBuf, uint outLen, out uint returned, IntPtr overlapped);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr lib, string name);

        // D3DKMT (gdi32.dll): open the GPU adapter by LUID, then query
        // ADAPTERPERFDATA (type 62) for temperature (decidegrees C) and
        // power (deciwatts). Same structs TraySense uses (kmt_perf).
        [StructLayout(LayoutKind.Sequential)]
        public struct KmtLuid { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KmtOpen { public KmtLuid Luid; public uint Adapter; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KmtQai { public uint Adapter; public uint Type; public IntPtr Data; public uint Size; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KmtPerf
        {
            public uint PhysIdx;
            public ulong MemFreq, MaxMemFreq, MaxMemFreqOC, MemBw, PcieBw;
            public uint FanRPM, Power, Temperature;
            public byte PowerStateOverride;
        }
        [DllImport("gdi32.dll")]
        public static extern int D3DKMTOpenAdapterFromLuid(ref KmtOpen open);
        [DllImport("gdi32.dll")]
        public static extern int D3DKMTQueryAdapterInfo(ref KmtQai query);
    }
}
