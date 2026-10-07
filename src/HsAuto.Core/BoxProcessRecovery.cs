using System.Diagnostics;

namespace HsAuto.Open;

internal sealed record BoxProcessIdentity(int Pid, long StartedUtcTicks, string Path);
internal interface IBoxProcessPlatform
{
    IReadOnlyList<BoxProcessIdentity> Snapshot();
    bool FileExists(string path);
    void CloseWindow(BoxProcessIdentity identity);
    void Terminate(BoxProcessIdentity identity);
    void Launch(string path);
}

internal sealed class WindowsBoxProcessPlatform : IBoxProcessPlatform
{
    public IReadOnlyList<BoxProcessIdentity> Snapshot()
    {
        var list = new List<BoxProcessIdentity>();
        foreach (var process in Process.GetProcessesByName("HSAng"))
        {
            using (process)
            {
                try { if (!process.HasExited) list.Add(new(process.Id, process.StartTime.ToUniversalTime().Ticks, process.MainModule?.FileName ?? "")); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    // Unreadable is not absent: retain a sentinel and refuse to operate on it.
                    list.Add(new(process.Id, 0, ""));
                }
            }
        }
        return list;
    }
    public bool FileExists(string path) => File.Exists(path);
    static Process Verified(BoxProcessIdentity identity)
    {
        var process = Process.GetProcessById(identity.Pid);
        try
        {
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != identity.StartedUtcTicks ||
                !string.Equals(process.MainModule?.FileName, identity.Path, StringComparison.OrdinalIgnoreCase))
                throw new IOException("盒子进程身份已变化，不会关闭未知程序。");
            return process;
        }
        catch { process.Dispose(); throw; }
    }
    public void CloseWindow(BoxProcessIdentity identity) { using var process = Verified(identity); process.CloseMainWindow(); }
    public void Terminate(BoxProcessIdentity identity) { using var process = Verified(identity); process.Kill(entireProcessTree: false); }
    public void Launch(string path)
    {
        var start = new ProcessStartInfo(path) { WorkingDirectory = System.IO.Path.GetDirectoryName(path), UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" }) start.Environment[key] = "";
        start.Environment["NO_PROXY"] = "*";
        using var process = Process.Start(start) ?? throw new IOException("未能启动炉石盒子。");
    }
}

internal sealed class BoxProcessRecovery
{
    readonly IBoxProcessPlatform platform;
    readonly TimeProvider clock;
    readonly Func<TimeSpan, CancellationToken, Task> delay;
    public BoxProcessRecovery(IBoxProcessPlatform processPlatform = null, TimeProvider timeProvider = null,
        Func<TimeSpan, CancellationToken, Task> wait = null)
    {
        platform = processPlatform ?? new WindowsBoxProcessPlatform(); clock = timeProvider ?? TimeProvider.System;
        delay = wait ?? ((duration, token) => Task.Delay(duration, token));
    }
    internal static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) ||
            !string.Equals(System.IO.Path.GetFileName(path), "HSAng.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("无法确认炉石盒子主程序路径，不会自动关闭或启动程序。");
    }
    BoxProcessIdentity Single(string path)
    {
        var all = platform.Snapshot();
        if (all.Count > 1) throw new IOException("检测到多个炉石盒子，无法安全自动重启，请只保留一个盒子。");
        var current = all.SingleOrDefault();
        if (current != null && (current.StartedUtcTicks <= 0 || !string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("运行中的盒子路径或身份无法确认，不会关闭未知进程。");
        return current;
    }
    async Task<bool> WaitForExit(BoxProcessIdentity identity, TimeSpan timeout, CancellationToken token)
    {
        var deadline = clock.GetUtcNow() + timeout;
        do
        {
            token.ThrowIfCancellationRequested();
            var current = Single(identity.Path);
            if (current == null) return true;
            if (current != identity) throw new IOException("盒子已被其他操作重启，不会继续关闭新进程。");
            await delay(TimeSpan.FromMilliseconds(250), token);
        } while (clock.GetUtcNow() < deadline);
        return false;
    }
    public async Task StopAsync(string path, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ValidatePath(path);
        var current = Single(path); if (current == null) return;
        log?.Invoke("正在关闭炉石盒子，保留炉石与战网。");
        platform.CloseWindow(current);
        if (await WaitForExit(current, TimeSpan.FromSeconds(3), token)) return;
        token.ThrowIfCancellationRequested();
        if (Single(path) != current) throw new IOException("盒子身份已变化，取消关闭。");
        log?.Invoke("盒子未正常退出，正在结束已确认的盒子进程。");
        platform.Terminate(current);
        if (!await WaitForExit(current, TimeSpan.FromSeconds(3), token)) throw new IOException("盒子仍未退出，暂不启动第二个盒子。");
    }
    public async Task RestartAsync(string path, Func<bool> targetStillCurrent, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); ValidatePath(path);
        if (!platform.FileExists(path)) throw new FileNotFoundException("炉石盒子主程序不存在。", path);
        if (!targetStillCurrent()) throw new IOException("目标炉石身份已变化，取消盒子恢复。");
        var previous = Single(path);
        await StopAsync(path, log, token);
        token.ThrowIfCancellationRequested();
        if (!targetStillCurrent()) throw new IOException("目标炉石已退出，取消启动盒子。");
        if (Single(path) != null) throw new IOException("盒子已被其他操作启动，不再重复启动。");
        log?.Invoke("正在重新打开炉石盒子…"); platform.Launch(path);
        var deadline = clock.GetUtcNow().AddSeconds(20);
        do
        {
            token.ThrowIfCancellationRequested();
            if (!targetStillCurrent()) throw new IOException("目标炉石身份已变化，取消重新连接。");
            var current = Single(path);
            if (current != null && current != previous) return;
            await delay(TimeSpan.FromMilliseconds(250), token);
        } while (clock.GetUtcNow() < deadline);
        throw new TimeoutException("20秒内未发现重新启动的炉石盒子，等待下次恢复。");
    }
}
