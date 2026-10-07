using HsAuto.Open;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;

static class RecoveryCases
{
    static void Assert(bool value, string reason = "Recovery assertion failed") { if (!value) throw new Exception(reason); }
    static async Task Cancelled(Func<Task> work)
    { bool caught = false; try { await work(); } catch (OperationCanceledException) { caught = true; } Assert(caught); }
    static async Task Refused(Func<Task> work)
    { bool caught = false; try { await work(); } catch (IOException) { caught = true; } Assert(caught); }

    public static async Task RunAsync(Func<string, Func<Task>, Task> check, string temp)
    {
        await check("自动安装：完整插件按哈希确认后零写入跳过", async () =>
        {
            var files = Fixture(temp); BridgeInstaller.Install(files.Game, files.Bundle, files.Backup);
            string plugin = Path.Combine(Path.GetDirectoryName(files.Game)!, "BepInEx/plugins/HsAuto.OpenBridge/HsAuto.OpenBridge.dll");
            var written = File.GetLastWriteTimeUtc(plugin); int copies = 0;
            await PluginStartup.EnsureAsync(() => BridgeInstaller.IsCurrentInstallation(files.Game, files.Bundle), () => false,
                () => { copies++; return Task.CompletedTask; }, null, default);
            Assert(copies == 0 && File.GetLastWriteTimeUtc(plugin) == written);
        });
        await check("自动安装：缺失插件自动安装且退出游戏后继续同一启动请求", async () =>
        {
            var files = Fixture(temp); bool gameRunning = true; int copies = 0, waits = 0;
            await PluginStartup.EnsureAsync(() => BridgeInstaller.IsCurrentInstallation(files.Game, files.Bundle), () => gameRunning,
                () => { Assert(!gameRunning); copies++; BridgeInstaller.Install(files.Game, files.Bundle, files.Backup); return Task.CompletedTask; }, null, default,
                (time, token) => { waits++; gameRunning = false; return Task.CompletedTask; });
            Assert(waits == 1 && copies == 1 && BridgeInstaller.IsCurrentInstallation(files.Game, files.Bundle));
        });
        await check("自动安装：旧插件和缺少启动文件均不误报完整", () =>
        {
            var files = Fixture(temp); BridgeInstaller.Install(files.Game, files.Bundle, files.Backup);
            var root = Path.GetDirectoryName(files.Game)!;
            File.WriteAllText(Path.Combine(root, "BepInEx/plugins/HsAuto.OpenBridge/HsAuto.OpenBridge.dll"), "old");
            Assert(!BridgeInstaller.IsCurrentInstallation(files.Game, files.Bundle));
            BridgeInstaller.Install(files.Game, files.Bundle, files.Backup);
            File.Delete(Path.Combine(root, "winhttp.dll"));
            Assert(!BridgeInstaller.IsCurrentInstallation(files.Game, files.Bundle));
            BridgeInstaller.Install(files.Game, files.Bundle, files.Backup);
            Assert(BridgeInstaller.IsCurrentInstallation(files.Game, files.Bundle)); return Task.CompletedTask;
        });
        await check("自动安装：等待和安装完成后的取消均不进入游戏操作", async () =>
        {
            using var cancel = new CancellationTokenSource(); int writes = 0;
            await Cancelled(() => PluginStartup.EnsureAsync(() => false, () => true,
                () => { writes++; return Task.CompletedTask; }, null, cancel.Token,
                (time, token) => { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }));
            Assert(writes == 0);
            using var during = new CancellationTokenSource(); bool finished = false;
            await Cancelled(() => PluginStartup.EnsureAsync(() => false, () => false,
                async () => { during.Cancel(); await Task.Yield(); finished = true; }, null, during.Token));
            Assert(finished);
        });
        await check("自动安装：失败三秒后修复重试且不提前报告成功", async () =>
        {
            bool ready = false; int attempts = 0; var waits = new List<TimeSpan>();
            await PluginStartup.EnsureAsync(() => ready, () => false,
                () => { if (++attempts == 1) throw new IOException("fixture copy failed"); ready = true; return Task.CompletedTask; }, null, default,
                (duration, token) => { waits.Add(duration); return Task.CompletedTask; });
            Assert(attempts == 2 && waits.SequenceEqual(new[] { TimeSpan.FromSeconds(3) }));
        });
        await check("恢复：8秒连接证据超时直接请求盒子重启", async () =>
        {
            int generations = 0, restarts = 0;
            var supervisor = new ScriptRecoverySupervisor(wait: (time, token) => Task.CompletedTask);
            await supervisor.RunAsync(token => { if (++generations == 1) throw new NeteaseBoxBindingTimeoutException("fixture 8s"); return Task.CompletedTask; },
                token => { restarts++; return Task.FromResult(true); }, true, null, default);
            Assert(generations == 2 && restarts == 1);
        });
        await check("恢复：确认健康进度重置连续次数但不清除盒子冷却", async () =>
        {
            var clock = new RecoveryClock(); var waits = new List<TimeSpan>(); int attempts = 0, restarts = 0;
            var supervisor = new ScriptRecoverySupervisor(clock, (duration, token) => { waits.Add(duration); clock.Advance(duration); return Task.CompletedTask; });
            await supervisor.RunAsync(token =>
            {
                if (++attempts == 2) supervisor.MarkHealthy();
                if (attempts <= 9) throw new IOException("fixture"); return Task.CompletedTask;
            }, token => { restarts++; return Task.FromResult(true); }, true, null, default);
            Assert(restarts == 2 && waits.Any(t => t > TimeSpan.FromSeconds(3)), "Expected cooldown after second box attempt");
        });
        await check("恢复：只读始终不重启盒子且旧引擎与新引擎不重叠", async () =>
        {
            int attempts = 0, restarts = 0, active = 0;
            var supervisor = new ScriptRecoverySupervisor(wait: (duration, token) => Task.CompletedTask);
            await supervisor.RunAsync(async token =>
            { Assert(++active == 1); try { await Task.Yield(); if (++attempts < 8) throw new IOException("fixture"); } finally { active--; } },
                token => { restarts++; return Task.FromResult(true); }, false, null, default);
            Assert(attempts == 8 && restarts == 0 && active == 0);
        });
        await check("恢复：持续错误保留运行直到用户停止，取消后不再重启", async () =>
        {
            using var cancel = new CancellationTokenSource(); int generations = 0, boxes = 0;
            var supervisor = new ScriptRecoverySupervisor(wait: (duration, token) =>
            { if (generations >= 6) cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; });
            await Cancelled(() => supervisor.RunAsync(token => { generations++; throw new IOException("fixture"); },
                token => { boxes++; return Task.FromResult(true); }, true, null, cancel.Token));
            Assert(generations == 6 && boxes == 1);
        });
        await check("恢复：段位停止仍尊重用户设置而不是当作故障重启", async () =>
        {
            int boxes = 0;
            var supervisor = new ScriptRecoverySupervisor(wait: (duration, token) => throw new Exception("Should not wait"));
            await supervisor.RunAsync(token => throw new EnginePolicyCompletedException("已达到停止段位"),
                token => { boxes++; return Task.FromResult(true); }, true, null, default);
            Assert(boxes == 0);
        });
        foreach (var invalid in new[] { "", "HSAng.exe", @"C:\fixture\Other.exe" })
            await check("盒子恢复：拒绝未确认路径 " + (invalid.Length == 0 ? "空路径" : invalid), async () =>
            {
                var platform = new RecoveryPlatform(); var recovery = new BoxProcessRecovery(platform);
                await Refused(() => recovery.RestartAsync(invalid, () => true, null, default));
                Assert(platform.Closed == 0 && platform.Killed.Count == 0 && platform.Launches == 0);
            });
        await check("盒子恢复：关闭窗口失败只终止确认的盒子PID，不关闭游戏", async () =>
        {
            var platform = new RecoveryPlatform { IgnoreClose = true };
            var clock = new RecoveryClock();
            var recovery = new BoxProcessRecovery(platform, clock, (duration, token) => { clock.Advance(duration); return Task.CompletedTask; });
            await recovery.RestartAsync(platform.Path, () => true, null, default);
            Assert(platform.Killed.SequenceEqual(new[] { 10 }) && platform.Launches == 1 && platform.Closed == 1);
        });
        await check("盒子恢复：取消和炉石身份变化阻止后续启动", async () =>
        {
            using var cancel = new CancellationTokenSource();
            var platform = new RecoveryPlatform { OnClosed = () => cancel.Cancel() };
            await Cancelled(() => new BoxProcessRecovery(platform).RestartAsync(platform.Path, () => true, null, cancel.Token));
            Assert(platform.Launches == 0 && platform.Killed.Count == 0);
            platform = new RecoveryPlatform(); bool gamePresent = true; platform.OnClosed = () => gamePresent = false;
            await Refused(() => new BoxProcessRecovery(platform).RestartAsync(platform.Path, () => gamePresent, null, default));
            Assert(platform.Launches == 0);
        });
        await check("盒子恢复：PID重用或路径变化不能继续结束新进程", async () =>
        {
            var platform = new RecoveryPlatform { IgnoreClose = true };
            platform.OnClosed = () => platform.Processes[0] = platform.Processes[0] with { StartedUtcTicks = 99 };
            await Refused(() => new BoxProcessRecovery(platform).RestartAsync(platform.Path, () => true, null, default));
            Assert(platform.Killed.Count == 0 && platform.Launches == 0);
        });
        await check("盒子恢复：启动后出现多个实例不能误报重连成功", async () =>
        {
            var platform = new RecoveryPlatform { MultipleAfterLaunch = true };
            await Refused(() => new BoxProcessRecovery(platform).RestartAsync(platform.Path, () => true, null, default));
            Assert(platform.Processes.Count == 2 && platform.Killed.Count == 0);
        });
        await check("动作恢复：未确认提交跨重连保留门禁，局面改变才解锁", () =>
        {
            var guard = new PendingGameAction();
            var before = new ConstructedGameState { MatchId = "fixture", Phase = ConstructedPhase.LocalTurn, Hand = [new() { EntityId = "42" }] };
            guard.Begin(before, new() { Type = ConstructedActionType.PlayCard, SourceEntityId = "42" });
            Assert(!guard.Resolve(before) && guard.HasPending);
            Assert(!guard.Resolve(before with { Tags = new Dictionary<string, string> { ["reason"] = "bad read" } }));
            Assert(guard.Resolve(before with { Hand = [] }) && !guard.HasPending); return Task.CompletedTask;
        });
        await check("错误三秒：我方缺推荐触发恢复，正常对手回合不触发", () =>
        {
            var clock = new RecoveryClock(); var health = new RecommendationHealth(clock, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
            var state = new ConstructedGameState { MatchId = "fixture", Phase = ConstructedPhase.LocalTurn };
            var wait = new[] { new ConstructedAction { Type = ConstructedActionType.Wait } };
            Assert(health.Observe(state, wait, false) == null); clock.Advance(TimeSpan.FromMilliseconds(2999));
            Assert(health.Observe(state, wait, false) == null); clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert(health.Observe(state, wait, false)?.Contains("自动重新连接") == true);
            state = state with { Phase = ConstructedPhase.OpponentTurn }; clock.Advance(TimeSpan.FromHours(1));
            Assert(health.Observe(state, wait, false) == null); return Task.CompletedTask;
        });
        await check("玩家日志：恢复信息不误显示已停止且保持中文简短", () =>
        {
            Assert(PlayerLog.ForDisplay("[恢复] 运行异常，3秒后自动重新连接：Bridge timeout") == "运行暂时异常，3秒后自动重新连接。");
            Assert(PlayerLog.ForDisplay("[恢复] 正在自动重连脚本（1/3），不关闭游戏。").Contains("自动重连")); return Task.CompletedTask;
        });
    }
    static (string Game, string Bundle, string Backup) Fixture(string temp)
    {
        var root = Path.Combine(temp, Guid.NewGuid().ToString("N")); string gameRoot = Path.Combine(root, "Game"), bundle = Path.Combine(root, "Bundle");
        foreach (var dir in new[] { gameRoot, Path.Combine(bundle, "Bridge"), Path.Combine(bundle, "BepInEx.Runtime/BepInEx/core") }) Directory.CreateDirectory(dir);
        var game = Path.Combine(gameRoot, "Hearthstone.exe"); File.WriteAllText(game, "fixture; never executed");
        File.WriteAllText(Path.Combine(bundle, "Bridge/HsAuto.OpenBridge.dll"), "plugin fixture");
        File.WriteAllText(Path.Combine(bundle, "BepInEx.Runtime/BepInEx/core/BepInEx.dll"), "runtime fixture");
        File.WriteAllText(Path.Combine(bundle, "BepInEx.Runtime/winhttp.dll"), "loader fixture");
        File.WriteAllText(Path.Combine(bundle, "BepInEx.Runtime/doorstop_config.ini"), "config fixture");
        return (game, bundle, Path.Combine(root, "Backup"));
    }
    sealed class RecoveryClock : TimeProvider
    {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan time) => now += time;
    }
    sealed class RecoveryPlatform : IBoxProcessPlatform
    {
        public string Path { get; } = @"C:\fixture\HSAng.exe";
        public List<BoxProcessIdentity> Processes { get; } = [new(10, 10, @"C:\fixture\HSAng.exe")];
        public bool IgnoreClose, MultipleAfterLaunch;
        public int Launches, Closed;
        public List<int> Killed = [];
        public Action? OnClosed;
        public IReadOnlyList<BoxProcessIdentity> Snapshot() => Processes.ToArray();
        public bool FileExists(string path) => path == Path;
        public void CloseWindow(BoxProcessIdentity identity) { Closed++; if (!IgnoreClose) Processes.Remove(identity); OnClosed?.Invoke(); }
        public void Terminate(BoxProcessIdentity identity) { Killed.Add(identity.Pid); Processes.Remove(identity); }
        public void Launch(string path) { Launches++; Processes.Add(new(20, 20, path)); if (MultipleAfterLaunch) Processes.Add(new(21, 21, path)); }
    }
}
