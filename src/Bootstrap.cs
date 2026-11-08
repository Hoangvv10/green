using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

static class Bootstrap
{
    const string TargetName = "Bootstrap";
    static string TargetDir;
    static string LogPath;

    static int Main()
    {
        TargetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TargetName);
        LogPath = Path.Combine(TargetDir, "bootstrap.log");
        try
        {
            Directory.CreateDirectory(TargetDir);
            Log("===== Bootstrap bat dau =====");

            Extract();

            string setup = FindSetup();
            if (setup != null)
            {
                Log("Chay setup: " + setup);
                RunSetup(setup);
            }

            string exe = Path.Combine(TargetDir, "AutoPush.exe");
            if (!File.Exists(exe)) { Log("Khong tim thay AutoPush.exe sau khi giai nen."); return 1; }

            Run(exe, "--install");
            Run(exe, "--now");

            Log("===== Bootstrap xong =====");
            return 0;
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            return 1;
        }
    }

    static void Extract()
    {
        var asm = Assembly.GetExecutingAssembly();
        using (var rs = asm.GetManifestResourceStream("payload"))
        {
            if (rs == null) throw new Exception("Khong tim thay payload nhung trong exe.");
            using (var zip = new ZipArchive(rs, ZipArchiveMode.Read))
            {
                int n = 0;
                foreach (var e in zip.Entries)
                {
                    string dest = Path.GetFullPath(Path.Combine(TargetDir, e.FullName));
                    // chong path traversal
                    if (!dest.StartsWith(Path.GetFullPath(TargetDir) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase) &&
                        !dest.Equals(Path.GetFullPath(TargetDir), StringComparison.OrdinalIgnoreCase))
                    { Log("Bo qua entry dang ngo: " + e.FullName); continue; }

                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                    { Directory.CreateDirectory(dest); continue; }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    // Khong de ghi de config.json neu da ton tai (giu token/path da chinh tay)
                    if (Path.GetFileName(dest).Equals("config.json", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(dest))
                    { Log("Giu config.json da co, khong ghi de."); continue; }

                    using (var es = e.Open())
                    using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                        es.CopyTo(fs);
                    n++;
                }
                Log("Da giai nen " + n + " file -> " + TargetDir);
            }
        }
    }

    static string FindSetup()
    {
        foreach (var name in new[] { "autosetup.cmd", "autosetup.bat", "autosetup.ps1" })
        {
            string p = Path.Combine(TargetDir, name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    static void RunSetup(string path)
    {
        var psi = new ProcessStartInfo();
        psi.WorkingDirectory = TargetDir;
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ps1")
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + path + "\"";
        }
        else
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = "/c \"" + path + "\"";
        }
        using (var p = Process.Start(psi)) { p.WaitForExit(); Log("setup exit code " + p.ExitCode); }
    }

    static void Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args);
        psi.WorkingDirectory = TargetDir;
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        using (var p = Process.Start(psi)) { p.WaitForExit(); Log(Path.GetFileName(exe) + " " + args + " -> exit " + p.ExitCode); }
    }

    static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine); }
        catch { }
    }
}
