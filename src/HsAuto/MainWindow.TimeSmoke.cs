using System.IO;
using System.Text.Json;
using System.Windows;
using HsAuto.Open;

namespace HsAuto;

public partial class MainWindow
{
    // Only reachable from --smoke; all settings are in a temporary directory,
    // and every external operation is intercepted by SmokeRunStopPlatform.
    async Task SmokeTimedStopAsync(string folder)
    {
        if (!smoke || runStopPlatform is not SmokeRunStopPlatform platform)
            throw new IOException("Timer smoke requires the isolated fake platform.");
        var cases = new List<object>();
        var game = new[] { new StopProgramIdentity(42420, Path.Combine(data.DirectoryPath, "Hearthstone.exe"), DateTimeOffset.UnixEpoch) };
        var clients = new[] { new StopProgramIdentity(42421, Path.Combine(data.DirectoryPath, "HSAng.exe"), DateTimeOffset.UnixEpoch) };
        RunStopSettings Settings(RunStopAction action = RunStopAction.Shutdown) => new() { Enabled = true, Minutes = 1, Action = action };
        RunStopCountdown Expired()
        {
            var clock = new SmokeMonotonicClock(); var countdown = new RunStopCountdown(TimeSpan.FromSeconds(1), clock);
            clock.Advance(TimeSpan.FromSeconds(1)); return countdown;
        }
        static void Need(bool ok, string message) { if (!ok) throw new IOException(message); }
        async Task Check(string name, Func<Task> body)
        {
            platform.Events.Clear();
            try { await body(); cases.Add(new { name, passed = true }); }
            finally
            {
                if (timedStop is { } pending) { CancelTimedStop(false); await pending.MonitorTask; }
                run = null; activeEngineWork = null; activeGamePid = null;
                platform.ShutdownGate = null; platform.ShutdownEntered = null;
            }
        }
        async Task Complete(TimedStopSession session) => await session.MonitorTask.WaitAsync(TimeSpan.FromSeconds(3));
        try
        {
            await Check("到期只执行一次，先停止、保存再退出和关机", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                using var registration = source.Token.Register(() => platform.Events.Add("stop"));
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, game, clients, Expired(),
                    save: async () => { platform.Events.Add("save"); await data.SaveAsync(); });
                var session = timedStop!; await Complete(session);
                Need(platform.Events.SequenceEqual(new[] { "stop", "save", "close:2", "shutdown" }), "Expiry action order failed.");
                Need(timedStop == null && !session.Countdown.TryExpire(), "Timer fired more than once.");
            });
            await Check("取消倒计时不停止脚本，不产生外部操作", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, game, clients);
                var session = timedStop!; CancelTimedStopClick(this, new RoutedEventArgs()); await Complete(session);
                Need(!source.IsCancellationRequested && platform.Events.Count == 0 && timedStop == null, "Cancel incorrectly stopped run or apps.");
            });
            await Check("普通停止取消已到期但尚未执行的操作", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, game, clients, Expired());
                var session = timedStop!; StopClick(this, new RoutedEventArgs()); await Complete(session);
                Need(source.IsCancellationRequested && platform.Events.Count == 0, "Manual stop produced external effects.");
            });
            await Check("已取消的引擎仍正常保存并完成所选操作", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                BeginTimedStop(Settings(RunStopAction.ExitGame), source,
                    () => Task.FromCanceled(new CancellationToken(true)), game, clients, Expired(),
                    save: async () => { platform.Events.Add("save"); await data.SaveAsync(); });
                await Complete(timedStop!);
                Need(platform.Events.SequenceEqual(new[] { "save", "close:1" }), "Cancelled worker prevented safe save or closed clients unexpectedly.");
            });
            await Check("引擎停止超时不退出、不关机", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                BeginTimedStop(Settings(), source, () => worker.Task, game, clients, Expired(),
                    save: () => { platform.Events.Add("save"); return Task.CompletedTask; }, drainTimeout: TimeSpan.FromMilliseconds(30));
                await Complete(timedStop!); worker.TrySetResult();
                Need(platform.Events.Count == 0 && TimedStopCountdown.Text == "定时操作未完成", "Drain timeout produced external effects.");
            });
            await Check("保存失败不退出、不关机", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, game, clients, Expired(),
                    save: () => throw new IOException("offline save failure fixture"));
                await Complete(timedStop!);
                Need(platform.Events.Count == 0 && TimedStopCountdown.Text == "定时操作未完成", "Save failure produced external effects.");
            });
            await Check("游戏身份缺失先保存，但不退出和关机", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, [], clients, Expired(),
                    save: () => { platform.Events.Add("save"); return Task.CompletedTask; });
                await Complete(timedStop!);
                Need(platform.Events.SequenceEqual(new[] { "save" }) && TimedStopCountdown.Text == "定时操作未完成", "Missing identity was treated as safe shutdown.");
            });
            await Check("关机缓冲期可取消，不提交系统关机", async () =>
            {
                using var source = new CancellationTokenSource(); run = source;
                platform.ShutdownGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                platform.ShutdownEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, game, clients, Expired());
                var session = timedStop!; await platform.ShutdownEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Need(TimedStopCountdown.Text == "30秒后请求关机", "Shutdown grace hint missing.");
                CancelTimedStopClick(this, new RoutedEventArgs()); await Complete(session);
                Need(platform.Events.SequenceEqual(new[] { "close:2" }), "Grace cancellation submitted shutdown.");
            });
            await Check("本次运行保留设置快照，新会话不继承旧倒计时", async () =>
            {
                var clock = new SmokeMonotonicClock(); using var first = new CancellationTokenSource(); run = first;
                var settings = Settings();
                BeginTimedStop(settings, first, () => Task.CompletedTask, game, clients, new RunStopCountdown(TimeSpan.FromMinutes(1), clock));
                var old = timedStop!; settings.Action = RunStopAction.StopOnly; settings.Minutes = 9;
                Need(old.Settings.Action == RunStopAction.Shutdown && old.Settings.Minutes == 1, "Timer snapshot changed with settings.");
                CancelTimedStop(false); await Complete(old);
                clock.Advance(TimeSpan.FromHours(1));
                Need(!old.Countdown.TryExpire() && platform.Events.Count == 0, "Cancelled session resurrected.");
                using var second = new CancellationTokenSource(); run = second;
                BeginTimedStop(Settings(RunStopAction.StopOnly), second, () => Task.CompletedTask, [], [], Expired());
                await Complete(timedStop!);
                Need(second.IsCancellationRequested && !first.IsCancellationRequested && platform.Events.Count == 0, "Old timer affected the new run.");
            });
            await Check("未启用计时也可随时立即停止并执行所选操作", async () =>
            {
                using var source = new CancellationTokenSource(); run = source; activeGamePid = 42420;
                TimedStopActionCombo.SelectedIndex = (int)RunStopAction.ExitGame;
                Need(!data.Settings.Time.Enabled, "Smoke expected disabled timer.");
                await ImmediateStopAsync();
                Need(source.IsCancellationRequested && timedStop == null && platform.Events.SequenceEqual(new[] { "close:1" }), "Immediate action required countdown or failed.");
            });
            await Check("立即操作替代旧倒计时而不重复执行", async () =>
            {
                using var source = new CancellationTokenSource(); run = source; activeGamePid = 42420;
                BeginTimedStop(Settings(), source, () => Task.CompletedTask, game, clients);
                Need(ImmediateStopButton.IsEnabled, "Immediate action is inaccessible while countdown runs.");
                var old = timedStop!; TimedStopActionCombo.SelectedIndex = (int)RunStopAction.ExitGame;
                await ImmediateStopAsync();
                Need(old.MonitorTask.IsCompleted && source.IsCancellationRequested && timedStop == null && platform.Events.SequenceEqual(new[] { "close:1" }), "Immediate replacement performed two actions.");
            });
            File.WriteAllText(Path.Combine(folder, "time-smoke.json"), JsonSerializer.Serialize(new
            {
                scope = "Hidden WPF and fake platform only; no live apps closed or system shutdown invoked",
                passed = cases.Count, failed = 0, cases
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            TimedStopActionCombo.SelectedIndex = 0; platform.Events.Clear();
            UpdateTimedStopReadyText(); StartButton.IsEnabled = true; StopButton.IsEnabled = false;
        }
    }
    sealed class SmokeMonotonicClock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan value) => ticks += value.Ticks;
    }
}
