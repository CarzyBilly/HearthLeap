using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace HsAuto.Open;

public static class Locator
{
    static readonly string[] BoxExecutables = ["HSAng.exe", "HSBox.exe", "HearthstoneBox.exe"];
    const uint QueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    public static string NormalizePath(string? path) =>
        Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"'));

    public static string? ParseCommand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Regex.Match(text.Trim(), """^"([^"]+\.exe)"|^(.+?\.exe)(?:[,\s]|$)""", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return match.Success ? NormalizePath(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
    }

    public static bool ValidPath(string? path, bool game)
    {
        path = NormalizePath(path);
        return File.Exists(path) && (game
            ? string.Equals(Path.GetFileName(path), "Hearthstone.exe", StringComparison.OrdinalIgnoreCase)
            : BoxExecutables.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
    }

    internal static FoundApp SelectRunning(bool game, string? saved, IEnumerable<FoundApp> candidates)
    {
        var live = candidates.Where(c => c.Pid.HasValue && ValidPath(c.Path, game))
            .GroupBy(c => c.Pid).Select(g => g.First()).OrderBy(c => c.Pid).ToArray();
        var remembered = NormalizePath(saved);
        var matchingInstall = live.Where(c => string.Equals(c.Path, remembered, StringComparison.OrdinalIgnoreCase)).ToArray();
        var selected = matchingInstall.Length > 0 ? matchingInstall : live;
        if (selected.Length == 1) return selected[0];
        if (selected.Length > 1)
            return new(selected[0].Path, game ? "多个炉石实例，请确认目标" : "多个盒子实例，请确认目标");
        return new("", "未找到");
    }

    public static FoundApp Find(bool game, string? saved = null)
    {
        var running = new List<FoundApp>();
        foreach (var name in game ? new[] { "Hearthstone" } : BoxExecutables.Select(Path.GetFileNameWithoutExtension))
        foreach (var process in Process.GetProcessesByName(name!))
        {
            using (process)
            {
                try
                {
                    if (process.HasExited) continue;
                    var path = ProcessPath(process);
                    if (ValidPath(path, game)) running.Add(new(NormalizePath(path), "运行中", process.Id));
                }
                catch (Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        var live = SelectRunning(game, saved, running);
        if (live.Found) return live;
        var remembered = NormalizePath(saved);
        if (ValidPath(remembered, game)) return new(remembered, "已保存的位置");
        foreach (var path in RegisteredPaths(game))
            if (ValidPath(path, game)) return new(NormalizePath(path), "安装注册表");
        var roots = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        }.Concat(DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .SelectMany(d => new[] { d.Name, Path.Combine(d.Name, "Games"), Path.Combine(d.Name, "游戏"), Path.Combine(d.Name, "Program Files"), Path.Combine(d.Name, "Program Files (x86)") }))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        foreach (var folder in game ? new[] { "Hearthstone", "炉石传说" } : new[] { "炉石传说盒子", @"网易\炉石传说盒子", @"Netease\HSAng", "HSAng" })
        foreach (var exe in game ? new[] { "Hearthstone.exe" } : BoxExecutables)
        {
            var path = Path.Combine(root, folder, exe);
            if (ValidPath(path, game)) return new(path, "常见安装目录");
        }
        return new("", "未找到");
    }

    static string? ProcessPath(Process process)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, process.Id);
        if (handle != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(32768);
                uint capacity = (uint)buffer.Capacity;
                if (QueryFullProcessImageName(handle, 0, buffer, ref capacity)) return buffer.ToString();
            }
            finally { CloseHandle(handle); }
        }
        try { return process.MainModule?.FileName; }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    static IEnumerable<string> RegisteredPaths(bool game)
    {
        var result = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                foreach (var exe in game ? new[] { "Hearthstone.exe" } : BoxExecutables)
                {
                    using var app = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                    var command = ParseCommand(app?.GetValue("")?.ToString());
                    if (command != null) result.Add(command);
                }
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    try
                    {
                        using var entry = uninstall!.OpenSubKey(name);
                        if (entry == null) continue;
                        var display = entry.GetValue("DisplayName")?.ToString() ?? "";
                        var matches = game
                            ? display.Contains("Hearthstone", StringComparison.OrdinalIgnoreCase) || display == "炉石传说"
                            : display.Contains("炉石传说盒子") || display.Contains("HSAng", StringComparison.OrdinalIgnoreCase);
                        if (!matches) continue;
                        var location = NormalizePath(entry.GetValue("InstallLocation")?.ToString());
                        foreach (var exe in game ? new[] { "Hearthstone.exe" } : BoxExecutables)
                            if (location.Length > 0) result.Add(Path.Combine(location, exe));
                        var icon = ParseCommand(entry.GetValue("DisplayIcon")?.ToString());
                        if (icon != null) result.Add(icon);
                    }
                    catch (System.Security.SecurityException) { }
                    catch (UnauthorizedAccessException) { }
                }
                using var blizzard = root.OpenSubKey(@"Software\Blizzard Entertainment\Hearthstone");
                var install = NormalizePath(blizzard?.GetValue("InstallPath")?.ToString());
                if (game && install.Length > 0) result.Add(Path.Combine(install, "Hearthstone.exe"));
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    public static void Open(FoundApp app)
    {
        if (!app.Found) throw new FileNotFoundException("未找到程序。");
        if (app.Pid.HasValue) return;
        if (app.Source.StartsWith("多个", StringComparison.Ordinal)) throw new InvalidOperationException(app.Source + "，不会重复启动。");
        using var process = Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app.Path) });
    }
}
