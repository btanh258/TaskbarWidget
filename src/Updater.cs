using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BatteryBar
{
    // Self-update from GitHub releases: checks
    // https://api.github.com/repos/<owner>/<repo>/releases/latest,
    // downloads the TaskbarWidget.exe asset, and swaps it in with a
    // small batch script (the running exe cannot replace itself).
    // Automatic check: 10 s after start, then once a day. Manual check:
    // the "Check for updates" pill in Settings.
    sealed class Updater
    {
        const string ExeAsset = "TaskbarWidget.exe";

        AppContext app;

        public void Start(AppContext app)
        {
            this.app = app;
            var t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        void Loop()
        {
            try { Thread.Sleep(10000); } catch { return; }
            while (true)
            {
                try
                {
                    if (app.Settings.AutoUpdate && !Poller.Demo)
                        CheckAndPrompt();
                }
                catch { }
                try { Thread.Sleep(24 * 60 * 60 * 1000); } catch { return; }
            }
        }

        // Manual check from Settings: runs on a worker thread and reports
        // the outcome through done (the caller marshals it to the UI).
        public void CheckNow(Action<string> done)
        {
            var t = new Thread((ThreadStart)delegate
            {
                string status;
                try
                {
                    string version, url;
                    if (FindUpdate(out version, out url, out status))
                    {
                        PromptToUpdate(version, url);
                        if (done != null) done("Version " + version + " available");
                    }
                    else if (done != null) done(status);
                }
                catch (Exception e)
                {
                    Log("update check failed: " + e.Message);
                    if (done != null) done("Couldn't check for updates");
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void CheckAndPrompt()
        {
            string version, url, status;
            if (!FindUpdate(out version, out url, out status)) return;
            PromptToUpdate(version, url);
        }

        void PromptToUpdate(string version, string url)
        {
            app.UiBeginInvoke(delegate
            {
                if (MessageBox.Show(
                    "Taskbar Widget " + version + " is available (you have " + Program.Version + ").\n\nUpdate now?",
                    "Taskbar Widget", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var t = new Thread((ThreadStart)delegate { DownloadAndApply(url, version); });
                t.IsBackground = true;
                t.Start();
            });
        }

        // Returns true when a newer release exists. status explains the
        // no-update case ("You're up to date" / error text).
        bool FindUpdate(out string version, out string url, out string status)
        {
            version = null; url = null; status = null;
            string repo = Program.Repo.Replace("https://github.com/", "").TrimEnd('/');
            string api = "https://api.github.com/repos/" + repo + "/releases/latest";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(api);
            req.Method = "GET";
            req.UserAgent = "TaskbarWidget";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            string json;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                json = r.ReadToEnd();
            var ser = new JavaScriptSerializer();
            var root = ser.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) { status = "Couldn't check for updates"; return false; }
            object tag;
            if (!root.TryGetValue("tag_name", out tag) || tag == null) { status = "Couldn't check for updates"; return false; }
            version = ((tag as string) ?? "").Trim().TrimStart('v', 'V');
            int dash = version.IndexOf('-');
            if (dash >= 0) version = version.Substring(0, dash);
            if (CompareVersions(version, Program.Version) <= 0)
            {
                version = null;
                status = "You're on the latest version (v" + Program.Version + ")";
                return false;
            }
            object assets;
            if (root.TryGetValue("assets", out assets))
            {
                var list = assets as object[];
                if (list != null)
                    foreach (object a in list)
                    {
                        var d = a as Dictionary<string, object>;
                        if (d == null) continue;
                        object name, dl;
                        if (d.TryGetValue("name", out name) && d.TryGetValue("browser_download_url", out dl)
                            && (name as string) == ExeAsset && dl is string)
                        {
                            url = (string)dl;
                            return true;
                        }
                    }
            }
            status = "Version " + version + " is available, but no installer file was found";
            return false;
        }

        void DownloadAndApply(string url, string version)
        {
            string target = Application.ExecutablePath;
            string tmpDir = Path.GetTempPath();
            string tmpExe = Path.Combine(tmpDir, "TaskbarWidget-new.exe");
            string bat = Path.Combine(tmpDir, "TaskbarWidget-update.bat");
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "TaskbarWidget";
                req.Timeout = 30000;
                req.ReadWriteTimeout = 30000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var input = resp.GetResponseStream())
                using (var output = File.Create(tmpExe))
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = input.Read(buf, 0, buf.Length)) > 0) output.Write(buf, 0, n);
                }
                // Sanity: a real exe, not an error page.
                using (var f = File.OpenRead(tmpExe))
                {
                    if (f.Length < 100000) throw new Exception("download too small (" + f.Length + " bytes)");
                    byte[] mz = new byte[2];
                    if (f.Read(mz, 0, 2) != 2 || mz[0] != (byte)'M' || mz[1] != (byte)'Z')
                        throw new Exception("download is not an exe");
                }
                string script =
                    "@echo off\r\n" +
                    "set \"NEW=" + tmpExe + "\"\r\n" +
                    "set \"TARGET=" + target + "\"\r\n" +
                    "set /a n=0\r\n" +
                    ":retry\r\n" +
                    "copy /Y \"%NEW%\" \"%TARGET%\" >nul 2>&1\r\n" +
                    "if not errorlevel 1 goto done\r\n" +
                    "set /a n+=1\r\n" +
                    "if %n% geq 30 goto fail\r\n" +
                    "ping 127.0.0.1 -n 2 >nul\r\n" +
                    "goto retry\r\n" +
                    ":done\r\n" +
                    "start \"\" \"%TARGET%\"\r\n" +
                    "del \"%NEW%\" >nul 2>&1\r\n" +
                    ":fail\r\n" +
                    "(goto) 2>nul & del \"%~f0\"\r\n";
                File.WriteAllText(bat, script);
                var psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                Process.Start(psi);
                app.UiBeginInvoke(delegate { app.ExitThread(); });
            }
            catch (Exception e)
            {
                Log("update failed: " + e.Message);
                try { File.Delete(tmpExe); } catch { }
                app.UiBeginInvoke(delegate
                {
                    MessageBox.Show("Couldn't install the update: " + e.Message + "\n\nYou can still update manually from " + Program.Repo,
                        "Taskbar Widget", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                });
            }
        }

        static int CompareVersions(string a, string b)
        {
            string[] pa = (a ?? "").Split('.'), pb = (b ?? "").Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = 0, y = 0;
                if (i < pa.Length) int.TryParse(pa[i], out x);
                if (i < pb.Length) int.TryParse(pb[i], out y);
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        void Log(string s)
        {
            try { app.Sys.Log += s + "\r\n"; } catch { }
        }
    }
}
