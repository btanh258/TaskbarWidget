using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Windows.Forms;

namespace BatteryBar
{
    // PawnIO auto-install, ported from TraySense (traysense.c: pawnio_install).
// The driver lets the app read CPU temperature (MSR/SMN) and CPU package
// power (RAPL). It is NOT bundled: the official installer
// (namazso/PawnIO.Setup 2.2.0) is downloaded from GitHub and its SHA-256
// is verified before it runs, exactly like TraySense did. Installing a
// kernel driver needs administrator rights, so the app relaunches itself
// elevated (UAC) for the install only, then restarts unelevated.
static class PawnIo
{
    const string Url = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";

    // SHA-256 of PawnIO_setup.exe 2.2.0, copied from TraySense.
    static readonly byte[] Sha256 = new byte[]
    {
        0x1f, 0x51, 0x9a, 0x22, 0xe4, 0x71, 0x87, 0xf7, 0x0a, 0x13, 0x79, 0xa4, 0x8c, 0xa6, 0x04, 0x98,
        0x1c, 0x4f, 0xcf, 0x69, 0x4f, 0x4e, 0x65, 0xb7, 0x34, 0xaa, 0xa7, 0x4a, 0x9f, 0xba, 0x30, 0x32
    };

    // True when the driver answers (or is there but refuses us: it still
    // counts as installed, it just needs admin to open).
    public static bool Present()
    {
        try
        {
            using (var h = Native.Open(@"\\?\GLOBALROOT\Device\PawnIO", 0x80000000, false))
            {
                if (h == null || h.IsInvalid)
                    return Marshal.GetLastWin32Error() == 5; // ERROR_ACCESS_DENIED
                return true;
            }
        }
        catch { return false; }
    }

    public static bool IsAdmin()
    {
        try
        {
            var id = WindowsIdentity.GetCurrent();
            return id != null && new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // Download the official installer, verify its checksum, run it silently.
    // False with a reason in why on any failure. Exit code 0 or 3010
    // (reboot required) counts as success, like TraySense.
    public static bool Install(out string why)
    {
        why = "";
        string exe = Path.Combine(Path.GetTempPath(), "PawnIO_setup.exe");
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] bin;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "TaskbarWidget");
                bin = wc.DownloadData(Url);
            }
            if (bin == null || bin.Length == 0)
            {
                why = "Couldn't download the PawnIO installer from GitHub. Check your internet connection.";
                return false;
            }
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bin);
                if (hash.Length != Sha256.Length)
                {
                    why = "The downloaded PawnIO installer didn't match its known checksum, so it was not run.";
                    return false;
                }
                for (int i = 0; i < Sha256.Length; i++)
                {
                    if (hash[i] != Sha256[i])
                    {
                        why = "The downloaded PawnIO installer didn't match its known checksum, so it was not run.";
                        return false;
                    }
                }
            }
            File.WriteAllBytes(exe, bin);
            var psi = new ProcessStartInfo(exe, "-install -silent");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetTempPath();
            using (var p = Process.Start(psi))
            {
                if (p == null) { why = "Couldn't start the PawnIO installer."; return false; }
                if (!p.WaitForExit(180000))
                {
                    try { p.Kill(); } catch { }
                    why = "The PawnIO installer timed out.";
                    return false;
                }
                if (p.ExitCode != 0 && p.ExitCode != 3010) { why = "The PawnIO installer reported an error."; return false; }
            }
            return true;
        }
        catch (Exception e) { why = "Couldn't install PawnIO: " + e.Message; return false; }
        finally { try { File.Delete(exe); } catch { } }
    }

    // Relaunch this exe elevated (UAC prompt). True when the elevated
    // copy started; false when the user declined the prompt.
    public static bool RelaunchAdmin(string args)
    {
        try
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath, args);
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }

    // Restart this exe unelevated (via Explorer, which runs medium
    // integrity) after a short delay, so the elevated copy can exit
    // and release the single-instance mutex first.
    public static void RestartUnelevated()
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe",
                "/c ping 127.0.0.1 -n 3 > nul & explorer.exe \"" + Application.ExecutablePath + "\"");
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            Process.Start(psi);
        }
        catch { }
    }
}
}
