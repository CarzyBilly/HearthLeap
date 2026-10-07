namespace HsAuto.Open;

// No UI, no game launch and no process termination. All side effects are explicit
// delegates; tests can run the exact startup policy without touching an installation.
public static class PluginStartup
{
    public static async Task EnsureAsync(Func<bool> isCurrent, Func<bool> gameRunning,
        Func<Task> install, Action<string> log, CancellationToken token,
        Func<TimeSpan, CancellationToken, Task> wait = null)
    {
        wait ??= ((time, ct) => Task.Delay(time, ct));
        log?.Invoke("正在识别并智能安装插件…");
        bool reportedWaiting = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (await Task.Run(isCurrent, token))
                { log?.Invoke("插件已安装且完整，跳过安装。"); return; }
                if (await Task.Run(gameRunning, token))
                {
                    if (!reportedWaiting)
                    { log?.Invoke("插件需要安装或更新，请先退出炉石；软件会自动安装并等待重新启动，不必再次点击开始。"); reportedWaiting = true; }
                    await wait(TimeSpan.FromSeconds(1), token); continue;
                }
                token.ThrowIfCancellationRequested();
                log?.Invoke("未检测到完整插件，正在自动安装/修复插件…");
                // Installation is atomic and must drain even if cancellation arrives
                // during copying. The UI tracks the task as a critical operation.
                await install();
                token.ThrowIfCancellationRequested();
                if (!await Task.Run(isCurrent, token)) throw new IOException("安装后校验未通过。");
                log?.Invoke("插件安装完成。请正常启动炉石，脚本会自动继续。"); return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                log?.Invoke("插件检查/安装未完成，3秒后自动重试：" + ex.Message);
                await wait(TimeSpan.FromSeconds(3), token);
            }
        }
    }
}
