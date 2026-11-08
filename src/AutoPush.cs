// AutoPush - tu dong day code len GitHub qua REST API (khong can git o local).
// Build: xem build.ps1 (dung csc.exe co san trong .NET Framework 4.x cua Windows).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    const string AppName = "GitHubAutoPush";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static readonly string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
    static readonly string LogPath = Path.Combine(ExeDir, "autopush.log");
    static readonly string ConfigPath = Path.Combine(ExeDir, "config.json");
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static string Token;
    static string AuthorName, AuthorEmail;

    [STAThread]
    static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        try
        {
            if (mode == "--install") { Install(); return 0; }
            if (mode == "--uninstall") { Uninstall(); return 0; }

            bool created;
            using (var mutex = new Mutex(true, "Local\\" + AppName, out created))
            {
                if (!created) { Log("Da co 1 instance khac dang chay, thoat."); return 0; }
                Run(mode == "--now");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            return 1;
        }
    }

    // ---------------- Install / Uninstall (HKCU Run - khong can quyen admin) ----------------
    static void Install()
    {
        string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            key.SetValue(AppName, "\"" + exe + "\"");
        Log("Da dang ky chay khi khoi dong: " + exe);
        MessageBox.Show("Da cai dat AutoPush chay ngam moi khi dang nhap Windows.\n\n" + exe,
            AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static void Uninstall()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            if (key != null) key.DeleteValue(AppName, false);
        Log("Da go dang ky khoi dong.");
        MessageBox.Show("Da go AutoPush khoi danh sach khoi dong.", AppName,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ---------------- Main flow ----------------
    static void Run(bool now)
    {
        if (!File.Exists(ConfigPath)) { Log("Khong tim thay config.json tai " + ConfigPath); return; }
        var cfg = (Dictionary<string, object>)Json.DeserializeObject(File.ReadAllText(ConfigPath));

        Token = GetStr(cfg, "token", "");
        if (Token == "" || Token.StartsWith("ghp_xxx") || Token.StartsWith("github_pat_xxx"))
        { Log("Chua dien token trong config.json"); return; }
        AuthorName = GetStr(cfg, "authorName", "");
        AuthorEmail = GetStr(cfg, "authorEmail", "");

        int delay = GetInt(cfg, "startupDelaySeconds", 30);
        long maxBytes = (long)GetInt(cfg, "maxFileSizeMB", 50) * 1024 * 1024;
        var globalExclude = GetList(cfg, "exclude");

        Log("===== Bat dau (" + (now ? "chay tay" : "khoi dong") + ") =====");
        if (!now && delay > 0) Thread.Sleep(delay * 1000);
        if (!WaitForNetwork()) { Log("Khong co mang toi api.github.com, bo qua lan nay."); return; }

        object projectsObj;
        if (!cfg.TryGetValue("projects", out projectsObj) || !(projectsObj is object[]))
        { Log("config.json thieu mang 'projects'"); return; }

        foreach (Dictionary<string, object> p in (object[])projectsObj)
        {
            string path = GetStr(p, "path", "");
            string repo = GetStr(p, "repo", "");
            string branch = GetStr(p, "branch", "main");
            var excludes = new List<string>(globalExclude);
            excludes.AddRange(GetList(p, "exclude"));
            try
            {
                SyncProject(path, repo, branch, excludes, maxBytes);
            }
            catch (Exception ex)
            {
                Log("[" + repo + "] LOI: " + ex.Message);
            }
        }
        Log("===== Xong =====");
    }

    static bool WaitForNetwork()
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                int st; Api("GET", "https://api.github.com/rate_limit", null, out st);
                if (st == 200) return true;
                if (st == 401) { Log("Token khong hop le (401)."); return false; }
            }
            catch { }
            Thread.Sleep(15000);
        }
        return false;
    }

    // ---------------- Sync logic ----------------
    class Entry { public string Sha; public string Mode; public string FullPath; }

    static void SyncProject(string path, string repo, string branch, List<string> excludes, long maxBytes)
    {
        string tag = "[" + repo + "] ";
        if (path == "" || repo == "") throw new Exception("project thieu 'path' hoac 'repo'");
        string root = Path.GetFullPath(path);
        if (!Directory.Exists(root)) throw new Exception("thu muc khong ton tai: " + root);
        string api = "https://api.github.com/repos/" + repo;
        int st;

        // 1. Lay trang thai remote
        string headSha = null, headTree = null;
        var remote = new Dictionary<string, Entry>(StringComparer.Ordinal);

        var refObj = Api("GET", api + "/git/ref/heads/" + branch, null, out st);
        if (st == 409 || (st == 404 && IsRepoEmpty(api)))
        {
            Log(tag + "Repo rong -> khoi tao branch " + branch);
            var init = new Dictionary<string, object> {
                { "message", "Initialize repository" },
                { "content", Convert.ToBase64String(Encoding.UTF8.GetBytes("init\n")) },
                { "branch", branch } };
            Api("PUT", api + "/contents/.autopush-init", init, out st);
            if (st != 201 && st != 200) throw new Exception("khong khoi tao duoc repo rong (HTTP " + st + ")");
            refObj = Api("GET", api + "/git/ref/heads/" + branch, null, out st);
        }

        if (st == 200)
        {
            headSha = (string)((Dictionary<string, object>)refObj["object"])["sha"];
            var commit = Api("GET", api + "/git/commits/" + headSha, null, out st);
            Ensure(st, 200, "doc commit");
            headTree = (string)((Dictionary<string, object>)commit["tree"])["sha"];
            var tree = Api("GET", api + "/git/trees/" + headTree + "?recursive=1", null, out st);
            Ensure(st, 200, "doc tree");
            if (tree.ContainsKey("truncated") && (bool)tree["truncated"])
                Log(tag + "Canh bao: tree qua lon, GitHub tra ve bi cat bot.");
            foreach (Dictionary<string, object> e in (object[])tree["tree"])
                if ((string)e["type"] == "blob")
                    remote[(string)e["path"]] = new Entry { Sha = (string)e["sha"], Mode = (string)e["mode"] };
        }
        else if (st == 404)
        {
            var r = Api("GET", api, null, out st);
            if (st == 404) throw new Exception("repo khong ton tai hoac token khong co quyen");
            Log(tag + "Branch '" + branch + "' chua co -> se tao moi.");
        }
        else throw new Exception("doc ref that bai (HTTP " + st + ")");

        // 2. Quet local
        var ignore = new IgnoreRules(root, excludes);
        var local = new Dictionary<string, Entry>(StringComparer.Ordinal);
        int skipped = 0;
        ScanDir(root, root, ignore, local, remote, maxBytes, ref skipped, tag);

        if (local.Count == 0) { Log(tag + "Thu muc rong, bo qua de tranh xoa sach repo."); return; }

        // 3. So sanh
        int added = 0, modified = 0, deleted = 0;
        foreach (var kv in local)
        {
            Entry r;
            if (!remote.TryGetValue(kv.Key, out r)) added++;
            else if (r.Sha != kv.Value.Sha) modified++;
        }
        foreach (var k in remote.Keys) if (!local.ContainsKey(k)) deleted++;

        if (added + modified + deleted == 0) { Log(tag + "Khong co thay doi."); return; }
        Log(tag + "Thay doi: +" + added + " ~" + modified + " -" + deleted);

        // 4. Upload blob moi
        var remoteShas = new HashSet<string>(remote.Values.Select(v => v.Sha));
        var uploaded = new HashSet<string>();
        foreach (var e in local.Values)
        {
            if (e.FullPath == null || remoteShas.Contains(e.Sha) || uploaded.Contains(e.Sha)) continue;
            var body = new Dictionary<string, object> {
                { "content", Convert.ToBase64String(File.ReadAllBytes(e.FullPath)) },
                { "encoding", "base64" } };
            var res = Api("POST", api + "/git/blobs", body, out st);
            Ensure(st, 201, "upload blob " + e.FullPath);
            e.Sha = (string)res["sha"];
            uploaded.Add(e.Sha);
        }

        // 5. Tao tree (day du, khong base_tree -> file da xoa o local se bi xoa tren remote)
        var items = local.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (object)new Dictionary<string, object> {
            { "path", kv.Key }, { "mode", kv.Value.Mode }, { "type", "blob" }, { "sha", kv.Value.Sha } }).ToArray();
        var newTree = Api("POST", api + "/git/trees", new Dictionary<string, object> { { "tree", items } }, out st);
        Ensure(st, 201, "tao tree");
        string newTreeSha = (string)newTree["sha"];
        if (newTreeSha == headTree) { Log(tag + "Tree khong doi, bo qua."); return; }

        // 6. Commit
        var commitBody = new Dictionary<string, object> {
            { "message", string.Format("Auto-push tu {0} luc {1:yyyy-MM-dd HH:mm} (+{2} ~{3} -{4})",
                Environment.MachineName, DateTime.Now, added, modified, deleted) },
            { "tree", newTreeSha },
            { "parents", headSha == null ? new object[0] : new object[] { headSha } } };
        if (AuthorName != "" && AuthorEmail != "")
            commitBody["author"] = new Dictionary<string, object> {
                { "name", AuthorName }, { "email", AuthorEmail }, { "date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") } };
        var newCommit = Api("POST", api + "/git/commits", commitBody, out st);
        Ensure(st, 201, "tao commit");
        string newCommitSha = (string)newCommit["sha"];

        // 7. Cap nhat branch
        if (headSha == null)
        {
            Api("POST", api + "/git/refs", new Dictionary<string, object> {
                { "ref", "refs/heads/" + branch }, { "sha", newCommitSha } }, out st);
            Ensure(st, 201, "tao branch");
        }
        else
        {
            Api("PATCH", api + "/git/refs/heads/" + branch, new Dictionary<string, object> {
                { "sha", newCommitSha }, { "force", false } }, out st);
            Ensure(st, 200, "cap nhat branch");
        }
        Log(tag + "Da push commit " + newCommitSha.Substring(0, 7) + " len " + branch);
    }

    static bool IsRepoEmpty(string api)
    {
        int st;
        Api("GET", api + "/git/refs/heads", null, out st);
        return st == 409;
    }

    static void ScanDir(string root, string dir, IgnoreRules ignore, Dictionary<string, Entry> local,
        Dictionary<string, Entry> remote, long maxBytes, ref int skipped, string tag)
    {
        string[] files, dirs;
        try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
        catch (Exception ex) { Log(tag + "Khong doc duoc thu muc " + dir + ": " + ex.Message); return; }

        foreach (var d in dirs)
        {
            var attr = File.GetAttributes(d);
            if ((attr & FileAttributes.ReparsePoint) != 0) continue; // bo qua symlink/junction
            string rel = Rel(root, d);
            if (ignore.IsIgnored(rel, true)) continue;
            ScanDir(root, d, ignore, local, remote, maxBytes, ref skipped, tag);
        }
        foreach (var f in files)
        {
            string rel = Rel(root, f);
            if (ignore.IsIgnored(rel, false)) continue;
            Entry r;
            remote.TryGetValue(rel, out r);
            try
            {
                var fi = new FileInfo(f);
                if (fi.Length > maxBytes) { Log(tag + "Bo qua file qua lon: " + rel); skipped++; continue; }
                byte[] data = File.ReadAllBytes(f);
                local[rel] = new Entry { Sha = BlobSha(data), Mode = r != null ? r.Mode : "100644", FullPath = f };
            }
            catch (Exception ex)
            {
                // File dang bi khoa: giu nguyen ban tren remote (neu co) de khong vo tinh xoa
                Log(tag + "Khong doc duoc " + rel + ": " + ex.Message);
                if (r != null) local[rel] = new Entry { Sha = r.Sha, Mode = r.Mode, FullPath = null };
            }
        }
    }

    static string Rel(string root, string full)
    {
        return full.Substring(root.TrimEnd('\\').Length + 1).Replace('\\', '/');
    }

    static string BlobSha(byte[] data)
    {
        byte[] header = Encoding.ASCII.GetBytes("blob " + data.Length + "\0");
        using (var sha = SHA1.Create())
        {
            sha.TransformBlock(header, 0, header.Length, null, 0);
            sha.TransformFinalBlock(data, 0, data.Length);
            return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
        }
    }

    // ---------------- Ignore rules (exclude trong config + .gitignore o goc) ----------------
    class IgnoreRules
    {
        class Rule { public Regex Re; public bool DirOnly; public bool Anchored; }
        readonly List<Rule> rules = new List<Rule>();

        public IgnoreRules(string root, List<string> patterns)
        {
            var all = new List<string> { ".git" };
            all.AddRange(patterns);
            string gi = Path.Combine(root, ".gitignore");
            if (File.Exists(gi)) all.AddRange(File.ReadAllLines(gi));
            foreach (var raw in all)
            {
                string p = raw.Trim();
                if (p == "" || p.StartsWith("#") || p.StartsWith("!")) continue;
                bool dirOnly = p.EndsWith("/");
                p = p.TrimEnd('/');
                bool anchored = p.Contains("/");
                p = p.TrimStart('/');
                if (p == "") continue;
                string re = "^" + Regex.Escape(p).Replace(@"\*\*", "\u0001").Replace(@"\*", "[^/]*")
                    .Replace(@"\?", "[^/]").Replace("\u0001", ".*") + "$";
                rules.Add(new Rule { Re = new Regex(re, RegexOptions.IgnoreCase), DirOnly = dirOnly, Anchored = anchored });
            }
        }

        public bool IsIgnored(string rel, bool isDir)
        {
            string name = rel.Substring(rel.LastIndexOf('/') + 1);
            foreach (var r in rules)
            {
                if (r.DirOnly && !isDir) continue;
                if (r.Re.IsMatch(r.Anchored ? rel : name)) return true;
            }
            return false;
        }
    }

    // ---------------- HTTP ----------------
    static Dictionary<string, object> Api(string method, string url, object body, out int status)
    {
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = method;
        req.UserAgent = "AutoPush/1.0";
        req.Accept = "application/vnd.github+json";
        req.Headers["Authorization"] = "Bearer " + Token;
        req.Headers["X-GitHub-Api-Version"] = "2022-11-28";
        req.Timeout = 120000;
        req.ReadWriteTimeout = 300000;
        if (body != null)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(body));
            req.ContentType = "application/json";
            req.ContentLength = bytes.Length;
            using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
        }
        HttpWebResponse resp;
        try { resp = (HttpWebResponse)req.GetResponse(); }
        catch (WebException ex)
        {
            resp = ex.Response as HttpWebResponse;
            if (resp == null) throw;
        }
        using (resp)
        {
            status = (int)resp.StatusCode;
            string text;
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = sr.ReadToEnd();
            if (status >= 400 && status != 404 && status != 409)
                Log("HTTP " + status + " " + method + " " + url + ": " + Truncate(text, 300));
            if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();
            var obj = Json.DeserializeObject(text) as Dictionary<string, object>;
            return obj ?? new Dictionary<string, object>();
        }
    }

    static void Ensure(int actual, int expected, string what)
    {
        if (actual != expected) throw new Exception(what + " that bai (HTTP " + actual + ")");
    }

    // ---------------- Helpers ----------------
    static string GetStr(Dictionary<string, object> d, string k, string def)
    {
        object v; return d.TryGetValue(k, out v) && v != null ? v.ToString() : def;
    }
    static int GetInt(Dictionary<string, object> d, string k, int def)
    {
        object v; int i; return d.TryGetValue(k, out v) && v != null && int.TryParse(v.ToString(), out i) ? i : def;
    }
    static List<string> GetList(Dictionary<string, object> d, string k)
    {
        object v; var list = new List<string>();
        if (d.TryGetValue(k, out v) && v is object[]) foreach (var x in (object[])v) list.Add(x.ToString());
        return list;
    }
    static string Truncate(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "..."; }

    static readonly object LogLock = new object();
    static void Log(string msg)
    {
        lock (LogLock)
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2 * 1024 * 1024)
                    File.Delete(LogPath);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
