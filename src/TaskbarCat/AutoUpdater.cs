using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace TaskbarCat;

internal static class AutoUpdater
{
    private const string LatestReleaseApi = "https://api.github.com/repos/realr4an/taskbar-cat/releases/latest";
    private const string ExeAssetName = "TaskbarKatze.exe";
    private const string HashAssetName = "TaskbarKatze.exe.sha256";
    private const string VersionAssetName = "version.txt";

    public static async Task CheckAndApplyAsync(Control ui, Action closeApp)
    {
        try
        {
            await Task.Delay(3000);
            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || Path.GetFileName(currentExe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return;

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TaskbarCat-AutoUpdater/1.0");
            using var json = JsonDocument.Parse(await client.GetStringAsync(LatestReleaseApi));
            var assets = json.RootElement.GetProperty("assets").EnumerateArray()
                .ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("browser_download_url").GetString()!);
            if (!assets.TryGetValue(VersionAssetName, out var versionUrl) ||
                !assets.TryGetValue(ExeAssetName, out var exeUrl) ||
                !assets.TryGetValue(HashAssetName, out var hashUrl)) return;

            string versionText = (await client.GetStringAsync(versionUrl)).Trim();
            if (!Version.TryParse(versionText, out var available)) return;
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (available <= current) return;

            byte[] update = await client.GetByteArrayAsync(exeUrl);
            string expectedHash = (await client.GetStringAsync(hashUrl)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
            string actualHash = Convert.ToHexString(SHA256.HashData(update));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return;

            string updateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarCat", "Updates");
            Directory.CreateDirectory(updateDir);
            string stagedExe = Path.Combine(updateDir, $"TaskbarKatze-{available}.exe");
            await File.WriteAllBytesAsync(stagedExe, update);

            var start = new ProcessStartInfo(stagedExe) { UseShellExecute = false };
            start.ArgumentList.Add("--install-update");
            start.ArgumentList.Add(currentExe);
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            Process.Start(start);
            ui.BeginInvoke(closeApp);
        }
        catch
        {
            // Updates are deliberately silent; a network outage must never stop the cat.
        }
    }

    public static bool TryHandleInstallerMode(string[] args)
    {
        if (args.Length != 3 || args[0] != "--install-update" || !int.TryParse(args[2], out int oldPid)) return false;
        string target = Path.GetFullPath(args[1]);
        string self = Environment.ProcessPath!;
        try
        {
            try { Process.GetProcessById(oldPid).WaitForExit(20000); } catch { }
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try { File.Copy(self, target, true); break; }
                catch when (attempt < 29) { Thread.Sleep(250); }
            }
            var restart = new ProcessStartInfo(target) { UseShellExecute = true };
            restart.ArgumentList.Add("--cleanup-update");
            restart.ArgumentList.Add(self);
            Process.Start(restart);
        }
        catch { }
        return true;
    }

    public static void ScheduleCleanup(string[] args)
    {
        if (args.Length != 2 || args[0] != "--cleanup-update") return;
        string staged = args[1];
        _ = Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try { File.Delete(staged); return; }
                catch { await Task.Delay(250); }
            }
        });
    }
}
