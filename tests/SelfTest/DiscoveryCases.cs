using HsAuto.Open;

internal static class DiscoveryCases
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check, string temp)
    {
        static void Assert(bool ok) { if (!ok) throw new Exception("Discovery assertion failed."); }
        var root = Path.Combine(temp, "应用 中文 路径");
        Directory.CreateDirectory(root);
        var game = Path.Combine(root, "Hearthstone.exe");
        var box = Path.Combine(root, "HSAng.exe");
        File.WriteAllText(game, "isolated fixture");
        File.WriteAllText(box, "isolated fixture");
        await check("路径检测支持带空格中文环境变量和引号", () =>
        {
            Assert(Locator.ValidPath('"' + game + '"', true));
            Assert(Locator.ParseCommand('"' + box + "\",0") == box);
            Assert(Locator.ParseCommand(box + " --fixture") == box);
            Assert(!Locator.ValidPath(box, true) && !Locator.ValidPath(game, false));
            Assert(Locator.NormalizePath("%TEMP%") == Environment.GetEnvironmentVariable("TEMP"));
            return Task.CompletedTask;
        });
        await check("运行中候选去重且未读到路径不冒充已找到", () =>
        {
            var found = Locator.SelectRunning(true, null, [new(game, "运行中", 8), new(game, "重复", 8), new("", "无权限", 9)]);
            Assert(found.Pid == 8 && found.Path == game);
            Assert(!Locator.SelectRunning(true, null, [new("", "无权限", 9)]).Found);
            return Task.CompletedTask;
        });
        await check("多实例不按进程枚举顺序静默选择账号", () =>
        {
            var a = Locator.SelectRunning(true, game, [new(game, "运行中", 7), new(game, "运行中", 3)]);
            var b = Locator.SelectRunning(true, game, [new(game, "运行中", 3), new(game, "运行中", 7)]);
            Assert(a.Found && !a.Pid.HasValue && a.Source.StartsWith("多个") && a == b);
            return Task.CompletedTask;
        });
        await check("已保存安装目录可区分不同安装的单个客户端", () =>
        {
            var otherRoot = Path.Combine(root, "other"); Directory.CreateDirectory(otherRoot);
            var other = Path.Combine(otherRoot, "Hearthstone.exe"); File.WriteAllText(other, "fixture");
            var found = Locator.SelectRunning(true, game, [new(other, "运行中", 11), new(game, "运行中", 9)]);
            Assert(found.Pid == 9 && found.Path == game);
            return Task.CompletedTask;
        });
        await check("保存自动检测路径后重新打开无需重新定位", async () =>
        {
            var localRoot = Path.Combine(root, "data");
            var data = new LocalData(localRoot); await data.LoadAsync();
            data.Settings.BoxPath = box; data.Settings.GamePath = game; await data.SaveAsync();
            var reloaded = new LocalData(localRoot); await reloaded.LoadAsync();
            Assert(reloaded.Settings.BoxPath == box && reloaded.Settings.GamePath == game);
        });
    }
}
