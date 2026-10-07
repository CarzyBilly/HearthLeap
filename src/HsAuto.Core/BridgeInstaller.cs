using System.Diagnostics;
using System.Security.Cryptography;

namespace HsAuto.Open;

public static class BridgeInstaller
{
    public static bool IsInstalled(string gameExe)
    {
        if (!Locator.ValidPath(gameExe, true)) return false;
        var root = Path.GetDirectoryName(Path.GetFullPath(gameExe));
        var plugin = Path.Combine(root!, "BepInEx", "plugins", "HsAuto.OpenBridge", "HsAuto.OpenBridge.dll");
        return File.Exists(plugin) && new FileInfo(plugin).Length > 0 &&
            !File.GetAttributes(plugin).HasFlag(FileAttributes.ReparsePoint);
    }
    public static bool IsCurrentInstallation(string gameExe, string bundle)
    {
        if (!IsInstalled(gameExe)) return false;
        var root = Path.GetDirectoryName(Path.GetFullPath(gameExe));
        var plugin = Within(Path.Combine(root!, "BepInEx", "plugins", "HsAuto.OpenBridge", "HsAuto.OpenBridge.dll"), root);
        var expected = Path.Combine(bundle, "Bridge", "HsAuto.OpenBridge.dll");
        var required = new[] { "BepInEx/core/BepInEx.dll", "winhttp.dll", "doorstop_config.ini" };
        if (required.Any(file => !File.Exists(Within(Path.Combine(root!, file), root)))) return false;
        if (!File.Exists(expected)) throw new FileNotFoundException("本程序缺少插件安装文件，请保留完整的 Bridge 文件夹。", expected);
        return SHA256.HashData(File.ReadAllBytes(plugin)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(expected)));
    }
    public static bool GameIsRunning()
    {
        bool any = false;
        foreach (var process in Process.GetProcessesByName("Hearthstone"))
        {
            using (process)
            {
                try { if (!process.HasExited) any = true; }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { any = true; }
            }
        }
        return any;
    }
    sealed class RestoreManifest
    {
        public string GameRoot { get; set; } = "";
        public string[] CreatedFiles { get; set; } = [];
        public OriginalFile[] Originals { get; set; } = [];
    }
    sealed class OriginalFile
    {
        public string Backup { get; set; } = "";
        public string Destination { get; set; } = "";
    }
    static void EnsureGameClosed()
    {
        foreach (var game in Process.GetProcessesByName("Hearthstone"))
        {
            using (game) if (!game.HasExited) throw new InvalidOperationException("请先退出炉石，再安装、删除或恢复插件。不会自动关闭你的游戏。");
        }
    }
    static string Within(string path, string root)
    {
        var full = Path.GetFullPath(path); var parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("备份清单包含越界路径。");
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("文件是链接，不通过链接安装或恢复。");
        for (var current = Path.GetDirectoryName(full); current != null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("不通过目录链接安装或恢复，请选择实际安装目录。");
            if (current.Equals(parent, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }
    public static string Install(string gameExe, string bundle, string backupRoot)
    {
        if (!Locator.ValidPath(gameExe, true)) throw new FileNotFoundException("请先定位 Hearthstone.exe。");
        EnsureGameClosed();
        var gameRoot = Path.GetDirectoryName(Path.GetFullPath(gameExe));
        var pluginSource = Path.Combine(bundle, "Bridge", "HsAuto.OpenBridge.dll");
        if (!File.Exists(pluginSource)) throw new FileNotFoundException("安装包缺少开源 Bridge，请重新解压完整安装包。");
        var runtime = Path.Combine(bundle, "BepInEx.Runtime");
        var runtimeCore = Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll");
        if (!File.Exists(runtimeCore) && !Directory.Exists(runtime)) throw new FileNotFoundException("安装包缺少 BepInEx.Runtime。");
        var backup = Path.Combine(backupRoot, "bridge-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        var destinations = new List<string>(); var saved = new List<(string backup, string destination)>();
        void StoreOriginal(string dest)
        {
            if (!File.Exists(dest) || saved.Any(x => x.destination == dest)) return;
            Within(dest, gameRoot);
            var relative = Path.GetRelativePath(gameRoot, dest);
            var save = Path.Combine(backup, "original", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(save));
            File.Copy(dest, save, false); saved.Add((save, dest));
        }
        void CopyAtomic(string src, string dest)
        {
            Within(dest, gameRoot);
            StoreOriginal(dest); Directory.CreateDirectory(Path.GetDirectoryName(dest));
            var tmp = dest + ".hsauto-tmp";
            try {
                File.Copy(src, tmp, true);
                if (!SHA256.HashData(File.ReadAllBytes(src)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(tmp)))) throw new IOException("复制校验失败：" + Path.GetFileName(dest));
                File.Move(tmp, dest, true); destinations.Add(dest);
            } finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        try
        {
            // Preserve other software's plugins. Only remove the known legacy HsAuto plugin, after backup.
            var plugins = Path.Combine(gameRoot, "BepInEx", "plugins");
            if (Directory.Exists(plugins))
                foreach (var name in new[] { "hsmm.dll", "HsAuto.UnityBridge.Core.dll" })
                    foreach (var legacy in Directory.EnumerateFiles(plugins, name, SearchOption.AllDirectories)) { StoreOriginal(legacy); File.Delete(legacy); }
            if (!File.Exists(runtimeCore))
                foreach (var source in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories)) CopyAtomic(source, Path.Combine(gameRoot, Path.GetRelativePath(runtime, source)));
            else
            {
                var missing = new[] { "winhttp.dll", "doorstop_config.ini" }.Where(name => !File.Exists(Path.Combine(gameRoot, name))).ToArray();
                if (missing.Length > 0)
                {
                    var bundledCore = Path.Combine(runtime, "BepInEx", "core", "BepInEx.dll");
                    if (!File.Exists(bundledCore) || !SHA256.HashData(File.ReadAllBytes(runtimeCore)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(bundledCore))))
                        throw new IOException("已有其他版本的 BepInEx 且启动文件不完整；请先修复现有运行时，不覆盖其他插件管理器。");
                    foreach (var name in missing)
                    {
                        var source = Path.Combine(runtime, name);
                        if (!File.Exists(source)) throw new FileNotFoundException("缺少插件启动文件：" + name);
                        CopyAtomic(source, Path.Combine(gameRoot, name));
                    }
                }
            }
            CopyAtomic(pluginSource, Path.Combine(plugins, "HsAuto.OpenBridge", "HsAuto.OpenBridge.dll"));
            File.WriteAllText(Path.Combine(backup, "restore.json"), System.Text.Json.JsonSerializer.Serialize(new { GameRoot = gameRoot, CreatedFiles = destinations.Where(d => !saved.Any(s => s.destination == d)).ToArray(), Originals = saved.Select(s => new { Backup = s.backup, Destination = s.destination }).ToArray() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return "Bridge 安装完成。请通过战网打开炉石。备份：" + backup;
        }
        catch
        {
            foreach (var dest in destinations.Where(d => !saved.Any(s => s.destination == d))) if (File.Exists(dest)) File.Delete(dest);
            foreach (var item in saved) { Directory.CreateDirectory(Path.GetDirectoryName(item.destination)); File.Copy(item.backup, item.destination, true); }
            throw;
        }
    }
    public static string Uninstall(string gameExe, string backupRoot)
    {
        if (!Locator.ValidPath(gameExe, true)) throw new FileNotFoundException("请先定位 Hearthstone.exe。");
        EnsureGameClosed();
        var gameRoot = Path.GetDirectoryName(Path.GetFullPath(gameExe));
        var target = Within(Path.Combine(gameRoot, "BepInEx", "plugins", "HsAuto.OpenBridge", "HsAuto.OpenBridge.dll"), gameRoot);
        if (Directory.Exists(target)) throw new IOException("插件路径不是文件，未开始删除。");
        if (!File.Exists(target)) return "本项目插件未安装，无需删除。未改动 BepInEx 或其他插件。";
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("插件文件是链接，未开始删除，请使用实际安装目录。");
        var backup = Path.Combine(backupRoot, "plugin-deleted-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(backup);
        var original = Path.Combine(backup, "HsAuto.OpenBridge.dll");
        File.Copy(target, original);
        if (!SHA256.HashData(File.ReadAllBytes(target)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(original)))) throw new IOException("删除前备份校验失败，未删除插件。");
        var manifest = new RestoreManifest { GameRoot = gameRoot, Originals = new[] { new OriginalFile { Backup = original, Destination = target } } };
        var json = Path.Combine(backup, "restore.json");
        File.WriteAllText(json + ".tmp", System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.Move(json + ".tmp", json);
        File.Delete(target);
        return "已删除 HS Auto 开源插件。保留 BepInEx、其他插件及所有历史备份，不自动恢复旧 hsmm。删除前备份：" + backup;
    }
    public static string Restore(string manifestPath)
    {
        EnsureGameClosed();
        var backupRoot = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
        var manifest = System.Text.Json.JsonSerializer.Deserialize<RestoreManifest>(File.ReadAllText(manifestPath)) ?? throw new IOException("备份清单为空。");
        if (!Locator.ValidPath(Path.Combine(manifest.GameRoot, "Hearthstone.exe"), true)) throw new IOException("备份对应的炉石目录不存在，请不要移动备份或游戏目录。");
        var created = manifest.CreatedFiles.Select(p => Within(p, manifest.GameRoot)).ToArray();
        var originals = manifest.Originals.Select(p => (Source: Within(p.Backup, backupRoot), Destination: Within(p.Destination, manifest.GameRoot))).ToArray();
        foreach (var original in originals) if (!File.Exists(original.Source)) throw new FileNotFoundException("原始备份文件缺失，未开始恢复。", original.Source);
        var targets = created.Concat(originals.Select(p => p.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targets.Any(Directory.Exists)) throw new IOException("目标文件变成了目录，未开始恢复。");
        var safeguard = Path.Combine(backupRoot, "pre-restore-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(safeguard);
        var snapshots = targets.Select((target, i) => (Target: target, Copy: Path.Combine(safeguard, i + ".bin"), Existed: File.Exists(target))).ToArray();
        foreach (var item in snapshots) if (item.Existed) File.Copy(item.Target, item.Copy);
        try
        {
            foreach (var original in originals) { Directory.CreateDirectory(Path.GetDirectoryName(original.Destination)); File.Copy(original.Source, original.Destination, true); }
            foreach (var file in created) if (File.Exists(file)) File.Delete(file);
            return "已恢复该备份对应的安装前文件。其他插件未改动；原始备份仍保留。恢复前快照：" + safeguard;
        }
        catch
        {
            foreach (var item in snapshots)
            {
                if (item.Existed) File.Copy(item.Copy, item.Target, true);
                else if (File.Exists(item.Target)) File.Delete(item.Target);
            }
            throw;
        }
    }
}
