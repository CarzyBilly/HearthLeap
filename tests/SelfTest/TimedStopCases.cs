using System.Collections.Concurrent;
using HsAuto.Open;

internal static class TimedStopCases
{
    static void Need(bool ok, string message = "定时停止断言失败")
    {
        if (!ok) throw new Exception(message);
    }

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("定时停止：配置边界和零时长校验", () =>
        {
            var valid = new RunStopSettings { Enabled = true, Hours = 168, Minutes = 59, Action = RunStopAction.Shutdown };
            valid.Validate();
            Need(valid.Duration == TimeSpan.FromMinutes(168 * 60 + 59));

            foreach (var invalid in new[]
            {
                new RunStopSettings { Hours = -1 },
                new RunStopSettings { Hours = 169 },
                new RunStopSettings { Minutes = -1 },
                new RunStopSettings { Minutes = 60 },
                new RunStopSettings { Action = (RunStopAction)99 },
                new RunStopSettings { Enabled = true, Hours = 0, Minutes = 0 },
            })
            {
                var rejected = false;
                try { invalid.Validate(); }
                catch (IOException) { rejected = true; }
                Need(rejected, "非法时间配置没有被拒绝");
            }

            var normalized = new RunStopSettings { Enabled = true, Hours = 999, Minutes = -4, Action = (RunStopAction)(-1) };
            normalized.Normalize();
            Need(normalized.Hours == 168 && normalized.Minutes == 0 && normalized.Action == RunStopAction.StopOnly);
            Need(normalized.Enabled, "归一化后的有效时长错误禁用");
            var zero = new RunStopSettings { Enabled = true, Hours = -1, Minutes = -4 };
            zero.Normalize();
            Need(!zero.Enabled && zero.Duration == TimeSpan.Zero, "归一化后的零时长仍然启用");
            return Task.CompletedTask;
        });

        await check("定时停止：配置保存后重新加载", async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "HsAuto-runstop-" + Guid.NewGuid().ToString("N"));
            try
            {
                var data = new LocalData(root);
                await data.LoadAsync();
                data.Settings.Time = new RunStopSettings
                {
                    Enabled = true,
                    Hours = 2,
                    Minutes = 15,
                    Action = RunStopAction.ExitGameAndClients
                };
                await data.SaveAsync();

                var reloaded = new LocalData(root);
                await reloaded.LoadAsync();
                Need(reloaded.Settings.Time.Enabled && reloaded.Settings.Time.Hours == 2 && reloaded.Settings.Time.Minutes == 15);
                Need(reloaded.Settings.Time.Action == RunStopAction.ExitGameAndClients);
                Need(!File.Exists(Path.Combine(root, "settings.json.tmp")), "原子保存临时文件未清理");
            }
            finally { TryDelete(root); }
        });

        await check("定时停止：单调倒计时到期且只触发一次", () =>
        {
            var clock = new FakeMonotonicTimeProvider();
            var countdown = new RunStopCountdown(TimeSpan.FromSeconds(10), clock);
            Need(countdown.Remaining == TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(9.999));
            Need(!countdown.TryExpire() && countdown.Remaining > TimeSpan.Zero);
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Need(countdown.TryExpire(), "到期后未触发");
            Need(!countdown.TryExpire(), "同一个倒计时触发了两次");
            Need(countdown.Remaining == TimeSpan.Zero);
            clock.Advance(TimeSpan.FromMinutes(10));
            Need(!countdown.TryExpire(), "到期状态重复触发");
            return Task.CompletedTask;
        });

        await check("定时停止：手动停止取消倒计时", () =>
        {
            var clock = new FakeMonotonicTimeProvider();
            var countdown = new RunStopCountdown(TimeSpan.FromSeconds(1), clock);
            countdown.Cancel();
            clock.Advance(TimeSpan.FromMinutes(1));
            Need(countdown.Remaining == TimeSpan.Zero);
            Need(!countdown.TryExpire(), "手动停止后倒计时仍触发");
            countdown.Cancel(); // idempotent
            return Task.CompletedTask;
        });

        await check("定时停止：程序身份必须匹配PID、完整路径和启动时间", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "Hearthstone.exe");
            var started = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
            var identity = new StopProgramIdentity(1234, path, started);
            Need(identity.Matches(1234, Path.GetFullPath(path), started));
            Need(!identity.Matches(1235, path, started));
            Need(!identity.Matches(1234, path + ".renamed", started));
            Need(!identity.Matches(1234, path, started.AddSeconds(1)));
            Need(!identity.Matches(1234, "relative.exe", started));
            Need(!new StopProgramIdentity(0, path, started).Matches(0, path, started));
            return Task.CompletedTask;
        });

        await check("定时停止：成功顺序为停止、保存、退出、关机", async () =>
        {
            var events = new List<string>();
            var platform = new FakeRunStopPlatform(events);
            var game = Identity("game.exe", 2001);
            var clients = new[] { Identity("box.exe", 2002), Identity("battlenet.exe", 2003) };
            await RunStopSequence.ExecuteAsync(
                RunStopAction.Shutdown,
                () => { events.Add("stop"); return Task.CompletedTask; },
                () => { events.Add("save"); return Task.CompletedTask; },
                new[] { game }, clients, platform, _ => { }, default);
            Need(events.SequenceEqual(new[] { "stop", "save", "close:3", "shutdown" }),
                "定时停止安全顺序错误：" + string.Join(",", events));
        });

        await check("定时停止：仅停止脚本不产生外部关闭效果", async () =>
        {
            var events = new List<string>();
            var platform = new FakeRunStopPlatform(events);
            await RunStopSequence.ExecuteAsync(
                RunStopAction.StopOnly,
                () => { events.Add("stop"); return Task.CompletedTask; },
                () => { events.Add("save"); return Task.CompletedTask; },
                new[] { Identity("game.exe", 3001) }, Array.Empty<StopProgramIdentity>(), platform, events.Add, default);
            Need(events.SequenceEqual(new[] { "stop", "save" }));
            Need(platform.CloseCalls == 0 && platform.ShutdownCalls == 0);
        });

        await check("定时停止：停止或保存异常时不关闭程序、不关机", async () =>
        {
            foreach (var failAt in new[] { "stop", "save" })
            {
                var platform = new FakeRunStopPlatform();
                var externalCalls = 0;
                var failed = false;
                try
                {
                    await RunStopSequence.ExecuteAsync(
                        RunStopAction.Shutdown,
                        () => failAt == "stop" ? throw new IOException("stop fixture") : Task.CompletedTask,
                        () => failAt == "save" ? throw new IOException("save fixture") : Task.CompletedTask,
                        new[] { Identity("game.exe", 4001) }, Array.Empty<StopProgramIdentity>(),
                        platform, _ => externalCalls++, default);
                }
                catch (IOException) { failed = true; }
                Need(failed && platform.CloseCalls == 0 && platform.ShutdownCalls == 0 && externalCalls == 0,
                    failAt + "异常产生了外部副作用");
            }
        });

        await check("定时停止：取消令牌不会进入退出或关机", async () =>
        {
            var platform = new FakeRunStopPlatform();
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            var called = false;
            var rejected = false;
            try
            {
                await RunStopSequence.ExecuteAsync(
                    RunStopAction.Shutdown,
                    () => { called = true; return Task.CompletedTask; },
                    () => Task.CompletedTask,
                    new[] { Identity("game.exe", 5001) }, Array.Empty<StopProgramIdentity>(), platform, _ => { }, cancel.Token);
            }
            catch (OperationCanceledException) { rejected = true; }
            Need(rejected && !called && platform.CloseCalls == 0 && platform.ShutdownCalls == 0);
        });

        await check("定时停止：关机参数只验证，不执行真实命令", () =>
        {
            var schedule = WindowsRunStopPlatform.ShutdownCommand();
            var cancel = WindowsRunStopPlatform.ShutdownCommand(true);
            Need(schedule.FileName.EndsWith("shutdown.exe", StringComparison.OrdinalIgnoreCase));
            Need(schedule.Arguments.Contains("/s", StringComparison.OrdinalIgnoreCase));
            Need(schedule.Arguments.Contains("/t 0", StringComparison.OrdinalIgnoreCase));
            Need(!schedule.Arguments.Contains("/f", StringComparison.OrdinalIgnoreCase));
            Need(cancel.Arguments.Trim() == "/a");
            Need(!schedule.UseShellExecute && schedule.CreateNoWindow);
            return Task.CompletedTask;
        });

        await check("定时停止：无效进程参数不会捕获或关闭其他程序", () =>
        {
            Need(WindowsRunStopPlatform.Capture("", "").Count == 0);
            Need(WindowsRunStopPlatform.Capture("DefinitelyMissingHsAutoProcess", "relative.exe").Count == 0);
            Need(WindowsRunStopPlatform.Capture("DefinitelyMissingHsAutoProcess", "", 0).Count == 0);
            return Task.CompletedTask;
        });
        await check("定时停止：缺失或歧义游戏身份不触发外部动作", async () =>
        {
            var identity = Identity("game.exe", 7001);
            foreach (var targets in new IReadOnlyList<StopProgramIdentity>[]
            {
                [], [identity, identity with { StartedAt = identity.StartedAt.AddSeconds(1) }],
                [identity with { Path = "relative.exe" }]
            })
            {
                var events = new List<string>(); var platform = new FakeRunStopPlatform(events);
                bool failed = false;
                try
                {
                    await RunStopSequence.ExecuteAsync(RunStopAction.Shutdown,
                        () => { events.Add("stop"); return Task.CompletedTask; },
                        () => { events.Add("save"); return Task.CompletedTask; },
                        targets, [], platform, _ => { }, default);
                }
                catch (IOException) { failed = true; }
                Need(failed && events.SequenceEqual(new[] { "stop", "save" }));
            }
        });
        await check("定时停止：同PID的客户端身份冲突不继续关机", async () =>
        {
            var game = Identity("game.exe", 7101); var client = Identity("box.exe", 7102);
            var platform = new FakeRunStopPlatform(); bool failed = false;
            try
            {
                await RunStopSequence.ExecuteAsync(RunStopAction.Shutdown, () => Task.CompletedTask, () => Task.CompletedTask,
                    [game], [client, client with { Path = client.Path + ".other" }], platform, _ => { }, default);
            }
            catch (IOException) { failed = true; }
            Need(failed && platform.CloseCalls == 0 && platform.ShutdownCalls == 0);
        });
        await check("定时停止：客户端安全退出失败禁止继续关机", async () =>
        {
            var platform = new FakeRunStopPlatform { FailClose = true }; bool failed = false;
            try
            {
                await RunStopSequence.ExecuteAsync(RunStopAction.Shutdown, () => Task.CompletedTask, () => Task.CompletedTask,
                    [Identity("game.exe", 7201)], [], platform, _ => { }, default);
            }
            catch (IOException) { failed = true; }
            Need(failed && platform.CloseCalls == 1 && platform.ShutdownCalls == 0);
        });
        await check("定时停止：应用内关机缓冲期取消不会运行系统命令", async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
            var elapsed = System.Diagnostics.Stopwatch.StartNew(); bool cancelled = false;
            try { await new WindowsRunStopPlatform().ScheduleShutdownAsync(cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled && elapsed.Elapsed < WindowsRunStopPlatform.ShutdownGracePeriod);
        });
        await check("定时停止：保存后取消不会退出程序", async () =>
        {
            using var cancellation = new CancellationTokenSource(); var platform = new FakeRunStopPlatform(); bool cancelled = false;
            try
            {
                await RunStopSequence.ExecuteAsync(RunStopAction.Shutdown, () => Task.CompletedTask,
                    () => { cancellation.Cancel(); return Task.CompletedTask; },
                    [Identity("game.exe", 7301)], [], platform, _ => { }, cancellation.Token);
            }
            catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled && platform.CloseCalls == 0 && platform.ShutdownCalls == 0);
        });
    }

    static StopProgramIdentity Identity(string fileName, int pid)
        => new(pid, Path.Combine(Path.GetTempPath(), fileName), new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* test cleanup must not mask an assertion */ }
    }

    sealed class FakeMonotonicTimeProvider : TimeProvider
    {
        long timestamp;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta));
            Interlocked.Add(ref timestamp, delta.Ticks);
        }
    }

    sealed class FakeRunStopPlatform : IRunStopPlatform
    {
        readonly List<string> events;
        public int CloseCalls { get; private set; }
        public int ShutdownCalls { get; private set; }
        public bool FailClose { get; init; }
        public FakeRunStopPlatform(List<string>? events = null) => this.events = events ?? new();
        public Task CloseAsync(IReadOnlyList<StopProgramIdentity> programs, Action<string> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            CloseCalls++;
            if (FailClose) throw new IOException("offline close failure fixture");
            events.Add("close:" + programs.Count);
            return Task.CompletedTask;
        }
        public Task ScheduleShutdownAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ShutdownCalls++;
            events.Add("shutdown");
            return Task.CompletedTask;
        }
        public Task CancelShutdownAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            events.Add("cancel-shutdown");
            return Task.CompletedTask;
        }
    }
}
