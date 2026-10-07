using System.Diagnostics;

namespace HsAuto.Open;

public enum RunStopAction { StopOnly, ExitGame, ExitGameAndClients, Shutdown }

public sealed class RunStopSettings
{
    public bool Enabled { get; set; }
    public int Hours { get; set; }
    public int Minutes { get; set; }
    public RunStopAction Action { get; set; }
    public TimeSpan Duration => TimeSpan.FromMinutes((long)Hours * 60 + Minutes);
    public void Validate()
    {
        if (Hours is < 0 or > 168 || Minutes is < 0 or > 59 || !Enum.IsDefined(Action))
            throw new IOException("时间设置：小时需为0～168，分钟需为0～59，并选择有效的停止操作。");
        if (Enabled && Duration < TimeSpan.FromMinutes(1)) throw new IOException("启用定时停止后，请至少设置1分钟。");
    }
    public void Normalize()
    {
        Hours = Math.Clamp(Hours, 0, 168); Minutes = Math.Clamp(Minutes, 0, 59);
        if (!Enum.IsDefined(Action)) Action = RunStopAction.StopOnly;
        if (Duration < TimeSpan.FromMinutes(1)) Enabled = false;
    }
    public RunStopSettings Snapshot() => new() { Enabled = Enabled, Hours = Hours, Minutes = Minutes, Action = Action };
}

// Use only monotonic elapsed time: correcting the Windows clock must not stop a
// run early. The atomic state also prevents concurrent timer ticks firing twice.
public sealed class RunStopCountdown
{
    readonly TimeProvider clock;
    readonly long started;
    readonly TimeSpan duration;
    int state; // 0 = running, 1 = expired, 2 = cancelled
    public RunStopCountdown(TimeSpan duration, TimeProvider? clock = null)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        this.clock = clock ?? TimeProvider.System; this.duration = duration; started = this.clock.GetTimestamp();
    }
    public TimeSpan Remaining
    {
        get
        {
            if (Volatile.Read(ref state) != 0) return TimeSpan.Zero;
            var elapsed = clock.GetElapsedTime(started);
            return elapsed >= duration ? TimeSpan.Zero : elapsed <= TimeSpan.Zero ? duration : duration - elapsed;
        }
    }
    public void Cancel() => Interlocked.CompareExchange(ref state, 2, 0);
    public bool TryExpire()
    {
        if (Volatile.Read(ref state) != 0 || Remaining > TimeSpan.Zero) return false;
        return Interlocked.CompareExchange(ref state, 1, 0) == 0;
    }
}

public sealed record StopProgramIdentity(int Pid, string Path, DateTimeOffset StartedAt)
{
    public bool Matches(int pid, string path, DateTimeOffset startedAt)
    {
        if (Pid <= 0 || pid != Pid || StartedAt == default || StartedAt != startedAt ||
            string.IsNullOrWhiteSpace(Path) || string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            // Relative/empty paths are not proof of which installed program owns
            // a PID. Return false rather than throwing on a corrupted setting.
            return System.IO.Path.IsPathFullyQualified(Path) && System.IO.Path.IsPathFullyQualified(path) &&
                string.Equals(System.IO.Path.GetFullPath(Path), System.IO.Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }
}

public interface IRunStopPlatform
{
    Task CloseAsync(IReadOnlyList<StopProgramIdentity> programs, Action<string> log, CancellationToken token);
    Task ScheduleShutdownAsync(CancellationToken token);
    Task CancelShutdownAsync(CancellationToken token);
}

// Drain -> persist -> external effects. A failed drain/save never closes apps or
// shuts down; injectable platform lets tests prove this without touching games.
public static class RunStopSequence
{
    public static async Task ExecuteAsync(RunStopAction action, Func<Task> stopAndDrain, Func<Task> save,
        IReadOnlyList<StopProgramIdentity> game, IReadOnlyList<StopProgramIdentity> clients,
        IRunStopPlatform platform, Action<string> log, CancellationToken token)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        ArgumentNullException.ThrowIfNull(stopAndDrain); ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(game); ArgumentNullException.ThrowIfNull(clients);
        ArgumentNullException.ThrowIfNull(platform); ArgumentNullException.ThrowIfNull(log);
        // Freeze target identities before awaiting a stop operation; mutable UI
        // collections must not redirect the later close operation to a new run.
        var gameSnapshot = game.ToArray();
        var groups = (action == RunStopAction.ExitGame ? gameSnapshot : gameSnapshot.Concat(clients))
            .ToArray().GroupBy(p => p.Pid).ToArray();
        var targets = groups.Select(group => group.First()).ToArray();
        var ambiguous = groups.Any(group => group.Select(p => (p.Path, p.StartedAt)).Distinct().Count() != 1);
        token.ThrowIfCancellationRequested(); await stopAndDrain();
        token.ThrowIfCancellationRequested(); await save(); token.ThrowIfCancellationRequested();
        if (action == RunStopAction.StopOnly) return;
        // Missing/ambiguous identities are not equivalent to programs already
        // having exited. Stop and save, but fail closed before external effects.
        if (gameSnapshot.Length != 1 || ambiguous || targets.Any(p => !p.Matches(p.Pid, p.Path, p.StartedAt)))
            throw new IOException("未能安全确认本次游戏或客户端，已停止并保存；不会退出其他程序或继续关机。");
        await platform.CloseAsync(targets, log, token); token.ThrowIfCancellationRequested();
        if (action == RunStopAction.Shutdown)
        {
            log("数据已保存，30秒后请求关机；现在仍可取消本次操作。");
            await platform.ScheduleShutdownAsync(token);
        }
    }
}

public sealed class WindowsRunStopPlatform : IRunStopPlatform
{
    public static TimeSpan ShutdownGracePeriod => TimeSpan.FromSeconds(30);
    public static ProcessStartInfo ShutdownCommand(bool cancel = false) => new()
    {
        FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"),
        // Windows implicitly adds /f when /t is greater than zero. Keep the
        // grace period in this application instead, then request /t 0 without
        // /f so unrelated applications can still veto or prompt to save work.
        Arguments = cancel ? "/a" : "/s /t 0 /c \"HearthLeap 定时停止，已保存本地设置与战绩\"",
        UseShellExecute = false, CreateNoWindow = true
    };
    public async Task ScheduleShutdownAsync(CancellationToken token)
    {
        // Cancelling or closing HearthLeap during this grace period never
        // submits a Windows shutdown command; no system-wide /a is required.
        await Task.Delay(ShutdownGracePeriod, token);
        token.ThrowIfCancellationRequested();
        await CommandAsync(false, token);
    }
    public async Task CancelShutdownAsync(CancellationToken token) => await CommandAsync(true, token);
    static async Task CommandAsync(bool cancel, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(ShutdownCommand(cancel)) ?? throw new IOException("无法启动Windows关机命令。");
        // Once submitted, wait for the command result even if the UI is closing,
        // so that callers do not report cancellation while shutdown was accepted.
        await process.WaitForExitAsync(CancellationToken.None);
        if (process.ExitCode != 0) throw new IOException(cancel ? "未能取消关机，可能没有待执行的关机任务。" : "Windows没有接受关机请求。请检查权限。");
    }
    public static IReadOnlyList<StopProgramIdentity> Capture(string processName, string path = "", int? onlyPid = null, bool requireSafe = false)
    {
        if (string.IsNullOrWhiteSpace(processName) || onlyPid is <= 0) return [];
        path ??= "";
        if (path.Length > 0 && !System.IO.Path.IsPathFullyQualified(path)) return [];
        var result = new List<StopProgramIdentity>();
        bool unsafeIdentity = false;
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || onlyPid.HasValue && onlyPid.Value != process.Id) continue;
                    // Keeping a handle open prevents this PID from being reused
                    // while its executable and start time are being checked.
                    _ = process.Handle;
                    if (process.HasExited) continue;
                    var actual = process.MainModule?.FileName;
                    if (actual == null || path.Length > 0 &&
                        !string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(actual), StringComparison.OrdinalIgnoreCase))
                    { unsafeIdentity = true; continue; }
                    result.Add(new(process.Id, actual, process.StartTime.ToUniversalTime()));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException) { unsafeIdentity = true; }
            }
        }
        // Multiple installations: do not guess which launcher owns this session.
        bool ambiguous = path.Length == 0 && result.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        if (requireSafe && (unsafeIdentity || ambiguous))
            throw new IOException("客户端身份或安装位置无法确定，请确认只运行本次使用的炉石、盒子和战网；不会关闭不明程序。");
        return ambiguous ? [] : result;
    }
    public async Task CloseAsync(IReadOnlyList<StopProgramIdentity> programs, Action<string> log, CancellationToken token)
    {
        var requested = new List<Process>();
        bool unsafeClose = false;
        void Failed(string message) { unsafeClose = true; log(message); }
        static bool Exited(Process? process)
        {
            try { return process is not null && process.HasExited; }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return false; }
        }
        try
        {
            foreach (var identity in programs)
            {
                token.ThrowIfCancellationRequested(); Process? process = null;
                try
                {
                    if (identity.Pid == Environment.ProcessId || !identity.Matches(identity.Pid, identity.Path, identity.StartedAt))
                    { Failed("程序身份无法安全确认，已跳过；不会强制结束进程或继续关机。"); continue; }
                    try { process = Process.GetProcessById(identity.Pid); }
                    catch (ArgumentException) { continue; } // Target already exited; this is a successful no-op.
                    _ = process.Handle; // Pin PID across validation and CloseMainWindow.
                    if (process.HasExited) continue;
                    if (!identity.Matches(process.Id, process.MainModule?.FileName ?? "", process.StartTime.ToUniversalTime()))
                    { Failed("程序身份已变化，已跳过；不会关闭其他程序或继续关机。"); continue; }
                    // Politely close exactly this recorded program. Never kill
                    // a process tree, bypass confirmation, or force unsaved data.
                    _ = process.CloseMainWindow();
                    // Launchers also own windowless worker processes. Keep
                    // them in the bounded wait: closing the main window may
                    // naturally terminate them a moment later. A false return
                    // alone is not proof of failure; any survivors still block
                    // shutdown after the shared timeout. Never call Kill.
                    requested.Add(process); process = null;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or IOException)
                { if (!Exited(process)) Failed("部分程序未能自动退出，请手动关闭；不会强制结束进程或继续关机。"); }
                finally { process?.Dispose(); }
            }
            // One shared bounded wait, not eight seconds per process. Cancellation
            // still interrupts promptly and can prevent a pending shutdown action.
            var waits = requested.Select(p => p.WaitForExitAsync(token)).ToArray();
            try { await Task.WhenAll(waits).WaitAsync(TimeSpan.FromSeconds(8), token); }
            catch (TimeoutException) { Failed("程序尚未退出，可能正在提示保存或确认；请手动关闭，不会强制结束或继续关机。"); }
            token.ThrowIfCancellationRequested();
            if (unsafeClose) throw new IOException("部分程序未能安全退出，已取消后续关机；设置和战绩已保存。");
        }
        finally { foreach (var process in requested) process.Dispose(); }
    }
}
