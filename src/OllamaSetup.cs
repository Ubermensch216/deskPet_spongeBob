using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace BikiniFriends
{
    /// <summary>
    /// AI(Ollama + 모델) 가 없는 PC 에서 설정창의 "AI 자동 설치" 가 부르는 곳. 일꾼 스레드에서 부른다.
    ///   서버 확인 → (없으면) 설치 파일 내려받기 · 서명 확인 · 조용히 설치 → 서버 켜기 → (없으면) 모델 받기
    /// 큰 내려받기 전에는 반드시 confirm 으로 사용자 동의를 받는다.
    /// </summary>
    internal static class OllamaSetup
    {
        internal const string InstallerUrl = "https://ollama.com/download/OllamaSetup.exe";
        internal const string Cancelled = "cancelled";

        /// <summary>자동 설치는 이 PC 의 서버(localhost)에만 쓴다. 다른 PC 의 서버에는 설치할 수 없다.</summary>
        internal static bool IsLocalEndpoint(string endpoint)
        {
            Uri u;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out u)) return false;
            return u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>설치된 ollama.exe 를 찾는다. 없으면 null.</summary>
        internal static string FindExe()
        {
            var dirs = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama")
            };
            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) if (d.Trim().Length > 0) dirs.Add(d.Trim());
            foreach (string d in dirs)
                try { string p = Path.Combine(d, "ollama.exe"); if (File.Exists(p)) return p; } catch { }
            return null;
        }

        /// <summary>
        /// 끝까지 해 본다. 성공하면 null, 사용자가 멈추면 <see cref="Cancelled"/>, 실패하면 이유를 돌려준다.
        /// report(글, 퍼센트(-1 이면 모름), 취소 가능 여부) · confirm(내려받기 안내글) → 계속할지.
        /// </summary>
        internal static string Run(ChatOptions o, ChatRequest h, Action<string, int, bool> report, Func<string, bool> confirm)
        {
            report("지금 상태를 확인하는 중...", -1, true);
            ProbeResult r = OllamaClient.Probe(o);
            if (r == ProbeResult.Ok) return null;
            bool agreed = false;

            if (r == ProbeResult.Offline)
            {
                string exe = FindExe();
                if (exe == null)
                {
                    long free = FreeBytes(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    if (!confirm("이 PC 에는 Ollama 가 없어요.\n\n"
                        + "① Ollama 설치 파일(공식 ollama.com)을 내려받아 설치하고\n② AI 모델 '" + o.Model + "' 을 받아요.\n\n"
                        + "용량이 커서(합쳐서 수 GB 일 수 있어요) 시간이 걸려요. 인터넷 연결이 필요하고, 사용자 폴더의 여유 공간은 약 " + FmtBytes(free) + " 이에요.\n계속할까요?")) return Cancelled;
                    agreed = true;
                    string dir = Path.Combine(Path.GetTempPath(), "BikiniFriendsOllamaSetup_" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.CreateDirectory(dir);
                        string setup = Path.Combine(dir, "OllamaSetup.exe");
                        string err = Download(h, InstallerUrl, setup, report);
                        if (err != null) return err;
                        if (h.Cancelled) return Cancelled;
                        report("설치 파일의 서명을 확인하는 중...", -1, true);
                        err = VerifySignature(setup);
                        if (err != null) return err;
                        err = Install(setup, report);
                        if (err != null) return err;
                    }
                    finally { try { Directory.Delete(dir, true); } catch { } }
                    exe = FindExe();
                    if (exe == null) return "설치는 끝났는데 Ollama 를 찾지 못했어요. 컴퓨터를 다시 켠 뒤 다시 눌러 주세요.";
                }
                if (h.Cancelled) return Cancelled;
                string serr = WaitServer(o, exe, h, report);
                if (serr != null) return serr;
                r = OllamaClient.Probe(o);
            }

            if (r == ProbeResult.ModelMissing)
            {
                if (!agreed && !confirm("AI 모델 '" + o.Model + "' 을 내려받아요.\n\n용량이 커서(수 GB 일 수 있어요) 시간이 걸려요. 인터넷 연결이 필요해요.\n계속할까요?")) return Cancelled;
                string perr = Pull(o, h, report);
                if (perr != null) return perr;
            }

            report("마무리 확인 중...", -1, false);
            return OllamaClient.Probe(o) == ProbeResult.Ok ? null : "설치는 했는데 아직 연결이 안 돼요. 잠시 뒤 '연결 확인'을 눌러 보세요.";
        }

        // ───────── 내려받기 ─────────
        private static string Download(ChatRequest h, string url, string path, Action<string, int, bool> report)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = "BikiniFriends-DeskPet"; req.Timeout = 30000; req.ReadWriteTimeout = 30000;
                if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;
                h.Attach(req);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = File.Create(path))
                {
                    long total = resp.ContentLength, got = 0; int n, lastPct = -2; var buf = new byte[81920];
                    var tick = Stopwatch.StartNew(); long lastTick = -1000;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (h.Cancelled) return Cancelled;
                        dst.Write(buf, 0, n); got += n;
                        int pct = total > 0 ? (int)(got * 100 / total) : -1;
                        if (pct != lastPct && tick.ElapsedMilliseconds - lastTick >= 150)
                        {
                            lastPct = pct; lastTick = tick.ElapsedMilliseconds;
                            report("Ollama 설치 파일 내려받는 중... " + (total > 0 ? pct + "%  (" + FmtBytes(got) + " / " + FmtBytes(total) + ")" : FmtBytes(got)), pct, true);
                        }
                    }
                    if (total > 0 && got != total) return "내려받다가 연결이 끊겼어요. 인터넷 연결을 확인하고 다시 눌러 주세요.";
                }
                return null;
            }
            catch (Exception e) { return h.Cancelled ? Cancelled : "설치 파일을 내려받지 못했어요. 인터넷 연결을 확인해 주세요.\n(" + e.Message + ")"; }
        }

        // ───────── 서명 확인 (Authenticode) ─────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinTrustData
        {
            public int cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public int dwUIChoice; public int fdwRevocationChecks;
            public int dwUnionChoice; public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference;
            public int dwProvFlags; public int dwUIContext; public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

        /// <summary>파일에 올바른 디지털 서명이 있는지(변조 · 손상 여부 포함) 본다. 괜찮으면 null.</summary>
        internal static string VerifySignature(string path)
        {
            IntPtr pf = IntPtr.Zero, pd = IntPtr.Zero;
            try
            {
                var fi = new WinTrustFileInfo { cbStruct = Marshal.SizeOf(typeof(WinTrustFileInfo)), pcwszFilePath = path };
                pf = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo))); Marshal.StructureToPtr(fi, pf, false);
                var data = new WinTrustData { cbStruct = Marshal.SizeOf(typeof(WinTrustData)), dwUIChoice = 2 /*UI 없음*/, fdwRevocationChecks = 0, dwUnionChoice = 1 /*파일*/, pFile = pf, dwStateAction = 0 };
                pd = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustData))); Marshal.StructureToPtr(data, pd, false);
                int rc = WinVerifyTrust(new IntPtr(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), pd);
                return rc == 0 ? null : "내려받은 설치 파일의 디지털 서명이 올바르지 않아서 설치하지 않았어요. (코드 0x" + rc.ToString("X8") + ")";
            }
            catch (Exception e) { return "설치 파일의 서명을 확인하지 못해서 설치하지 않았어요. (" + e.Message + ")"; }
            finally { if (pd != IntPtr.Zero) Marshal.FreeHGlobal(pd); if (pf != IntPtr.Zero) Marshal.FreeHGlobal(pf); }
        }

        // ───────── 설치 · 서버 켜기 ─────────
        private static string Install(string setup, Action<string, int, bool> report)
        {
            report("Ollama 를 설치하는 중... (1~2분 걸려요. 취소할 수 없어요)", -1, false);
            try
            {
                using (Process p = Process.Start(new ProcessStartInfo(setup, "/SILENT /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true }))
                {
                    if (p == null) return "설치 프로그램을 실행하지 못했어요.";
                    if (!p.WaitForExit(15 * 60 * 1000)) return "설치가 너무 오래 걸려서 멈췄어요.";
                    if (p.ExitCode != 0) return "설치 프로그램이 오류로 끝났어요. (코드 " + p.ExitCode + ")";
                }
                return null;
            }
            catch (Win32Exception e) { return e.NativeErrorCode == 1223 ? "설치 허용을 취소했어요." : "설치 프로그램을 실행하지 못했어요. (" + e.Message + ")"; }
            catch (Exception e) { return "설치 프로그램을 실행하지 못했어요. (" + e.Message + ")"; }
        }

        private static void StartServer(string exe)
        {
            try
            {
                string app = Path.Combine(Path.GetDirectoryName(exe), "ollama app.exe");
                if (File.Exists(app)) Process.Start(new ProcessStartInfo(app) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(app) });
                else Process.Start(new ProcessStartInfo(exe, "serve") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
        }

        /// <summary>서버가 뜰 때까지 기다린다(설치 직후 저절로 켜지기도 해서 몇 초 뒤에도 안 떠 있으면 직접 켠다).</summary>
        private static string WaitServer(ChatOptions o, string exe, ChatRequest h, Action<string, int, bool> report)
        {
            var sw = Stopwatch.StartNew(); bool started = false;
            while (sw.Elapsed.TotalSeconds < 45)
            {
                if (h.Cancelled) return Cancelled;
                if (OllamaClient.Probe(o) != ProbeResult.Offline) return null;
                if (!started && sw.Elapsed.TotalSeconds >= 5) { started = true; StartServer(exe); }
                report("Ollama 를 켜는 중...", -1, true);
                Thread.Sleep(800);
            }
            return "Ollama 를 켜지 못했어요. 시작 메뉴에서 Ollama 를 직접 실행한 뒤 다시 눌러 주세요.";
        }

        // ───────── 모델 받기 ─────────
        /// <summary>/api/pull 을 줄 단위 JSON 으로 받으며 레이어별 진행량을 모아 전체 퍼센트로 보여 준다.</summary>
        private static string Pull(ChatOptions o, ChatRequest h, Action<string, int, bool> report)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(o.Endpoint.TrimEnd('/') + "/api/pull");
                req.Method = "POST"; req.Timeout = 30000; req.ReadWriteTimeout = 180000; req.Proxy = null; req.UserAgent = "BikiniFriends-DeskPet";
                byte[] body = new UTF8Encoding(false).GetBytes("{\"model\":" + Json.Quote(o.Model) + ",\"stream\":true}");
                req.ContentType = "application/json"; req.ContentLength = body.Length;
                h.Attach(req);
                using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
                var totals = new Dictionary<string, double>(); var dones = new Dictionary<string, double>();
                bool success = false; int shown = 0;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (h.Cancelled) return Cancelled;
                        if (line.Length == 0) continue;
                        object j = Json.Parse(line);
                        string err = Json.Str(j, "error"); if (err != null) return "모델을 받지 못했어요. (" + err + ")";
                        string status = Json.Str(j, "status") ?? "", digest = Json.Str(j, "digest");
                        if (status == "success") { success = true; continue; }
                        if (digest != null && Json.Get(j, "total") is double)
                        {
                            totals[digest] = (double)Json.Get(j, "total");
                            dones[digest] = Json.Get(j, "completed") is double ? (double)Json.Get(j, "completed") : 0;
                            double t = 0, d = 0; foreach (double v in totals.Values) t += v; foreach (double v in dones.Values) d += v;
                            int pct = t > 0 ? (int)Math.Min(100, d * 100 / t) : -1;
                            if (pct >= 0) { pct = Math.Max(pct, shown); shown = pct; }
                            report("AI 모델 '" + o.Model + "' 받는 중... " + pct + "%  (" + FmtBytes((long)d) + " / " + FmtBytes((long)t) + ")", pct, true);
                        }
                        else if (status.StartsWith("pulling manifest")) report("모델 정보를 확인하는 중...", -1, true);
                        else if (status.StartsWith("verifying")) report("내려받은 모델을 확인하는 중...", -1, true);
                        else if (status.StartsWith("writing")) report("모델을 저장하는 중...", -1, true);
                    }
                }
                return success ? null : "모델 받기가 중간에 끝났어요. 다시 누르면 이어서 받아요.";
            }
            catch (Exception e) { return h.Cancelled ? Cancelled : "모델을 받는 중에 연결이 끊겼어요. 다시 누르면 이어서 받아요.\n(" + e.Message + ")"; }
        }

        // ───────── 도우미 ─────────
        private static long FreeBytes(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(path)).AvailableFreeSpace; } catch { return 0; }
        }

        internal static string FmtBytes(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0") + " MB";
            return Math.Max(0, b / 1024) + " KB";
        }
    }
}