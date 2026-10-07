using HsAuto.Core.Automation;
using HsAuto.Core.Models;

namespace HsAuto.Open;

// Completion requested by a configured rule is not a runtime failure.
public sealed class EnginePolicyCompletedException(string message) : Exception(message);

// A missing recommendation is a distinct recovery signal.  The first signal
// restarts only the script generation; a second consecutive signal escalates
// to a safe box restart, after which the supervisor starts a fresh generation.
public sealed class RecommendationUnavailableException(string message) : IOException(message);

public sealed class ScriptRecoverySupervisor
{
    readonly TimeProvider clock;
    readonly Func<TimeSpan, CancellationToken, Task> delay;
    readonly TimeSpan boxCooldown;
    readonly TimeSpan boxWarmup;
    readonly Func<Exception, bool> retryable;
    DateTimeOffset? lastBoxAttempt;
    int consecutiveRestarts;
    public const int SoftRestartLimit = 3;
    public static readonly TimeSpan ErrorGrace = TimeSpan.FromSeconds(3);
    // HSAng.exe being present does not mean its web/overlay bridge is ready.
    // Give the box time to finish login, overlay loading and recommendation
    // page initialization before starting a new script generation.
    public static readonly TimeSpan DefaultBoxWarmup = TimeSpan.FromSeconds(10);

    public ScriptRecoverySupervisor(TimeProvider timeProvider = null,
        Func<TimeSpan, CancellationToken, Task> wait = null, TimeSpan? restartCooldown = null,
        Func<Exception, bool> shouldRetry = null, TimeSpan? boxWarmup = null)
    {
        clock = timeProvider ?? TimeProvider.System;
        delay = wait ?? ((duration, token) => Task.Delay(duration, token));
        boxCooldown = restartCooldown ?? TimeSpan.FromSeconds(120);
        boxWarmup = boxWarmup ?? DefaultBoxWarmup;
        this.boxWarmup = boxWarmup.Value;
        retryable = shouldRetry ?? (_ => true);
        if (boxCooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(restartCooldown));
        if (this.boxWarmup < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(boxWarmup));
    }
    // Only a confirmed action (or actionable read-only recommendation) is evidence of recovery.
    public void MarkHealthy() => consecutiveRestarts = 0;

    public async Task RunAsync(Func<CancellationToken, Task> generation,
        Func<CancellationToken, Task<bool>> restartBox, bool allowBoxRestart,
        Action<string> log, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { await generation(token); return; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (EnginePolicyCompletedException ex) { log?.Invoke(ex.Message); return; }
            catch (Exception ex)
            {
                token.ThrowIfCancellationRequested();
                if (!retryable(ex)) throw;
                bool alreadyTimedOut = (ex.Message ?? "").Contains("自动重新连接", StringComparison.Ordinal) ||
                    (ex.Message ?? "").Contains("自动重连", StringComparison.Ordinal);
                log?.Invoke(alreadyTimedOut ? "[恢复] 运行异常，立即自动重新连接：" + ex.Message : "[恢复] 运行异常，3秒后自动重新连接：" + ex.Message);
                if (!alreadyTimedOut) await delay(ErrorGrace, token);
                bool bindingTimedOut = ex is NeteaseBoxBindingTimeoutException;
                bool recommendationUnavailable = ex is RecommendationUnavailableException;
                // A missing AI recommendation gets one script-generation restart
                // first.  If the next generation still has no recommendation,
                // restart only the confirmed box; the loop below then starts the
                // script generation again automatically.  Other runtime failures
                // retain the normal three-reconnect threshold.
                int boxRestartThreshold = recommendationUnavailable ? 1 : SoftRestartLimit;
                if (allowBoxRestart && (bindingTimedOut || consecutiveRestarts >= boxRestartThreshold))
                {
                    var remaining = lastBoxAttempt.HasValue ? boxCooldown - (clock.GetUtcNow() - lastBoxAttempt.Value) : TimeSpan.Zero;
                    if (remaining > TimeSpan.Zero)
                    {
                        log?.Invoke($"[恢复] 盒子恢复冷却中，{Math.Ceiling(remaining.TotalSeconds):0}秒后重试；脚本保持等待。");
                        await delay(remaining, token);
                    }
                    token.ThrowIfCancellationRequested();
                    lastBoxAttempt = clock.GetUtcNow();
                    log?.Invoke(bindingTimedOut ? "[恢复] 盒子连接超时，正在自动重启炉石盒子…" :
                        recommendationUnavailable ? "[恢复] 自动重启脚本后仍无炉石AI建议，正在自动重启炉石盒子…" :
                        "[恢复] 连续3次脚本重连仍未恢复，正在自动重启炉石盒子…");
                    try
                    {
                        if (await restartBox(token))
                        {
                            log?.Invoke($"[恢复] 盒子已重启，等待 {boxWarmup.TotalSeconds:0} 秒让盒子加载完成…");
                            await delay(boxWarmup, token);
                            token.ThrowIfCancellationRequested();
                            log?.Invoke("[恢复] 盒子加载等待完成，正在自动重启脚本并重新连接。");
                        }
                        else log?.Invoke("[恢复] 盒子未能安全重启，保持等待并继续重试；不会关闭炉石。");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception restartError) { log?.Invoke("[恢复] 盒子恢复失败，保持等待：" + restartError.Message); }
                    consecutiveRestarts = 0;
                }
                else
                {
                    consecutiveRestarts = Math.Min(consecutiveRestarts + 1, SoftRestartLimit);
                    log?.Invoke(recommendationUnavailable
                        ? "[恢复] 炉石AI建议超过3秒未到，正在自动重启脚本（首次），不关闭游戏。"
                        : $"[恢复] 正在自动重连脚本（{consecutiveRestarts}/3），不关闭游戏。");
                }
            }
            // A generation has completed/faulted before the next is allowed to start.
            token.ThrowIfCancellationRequested();
        }
    }
}

// Keep unresolved submissions across reconnection; never blindly replay an action.
internal sealed class PendingGameAction
{
    ConstructedGameState before;
    ConstructedAction action;
    public bool HasPending => before != null;
    public void Begin(ConstructedGameState state, ConstructedAction candidate) { before = state; action = candidate; }
    public void Clear() { before = null; action = null; }
    public bool Resolve(ConstructedGameState now)
    {
        if (before == null) return true;
        if (!ActionConfirmation.Changed(action, before, now)) return false;
        Clear(); return true;
    }
}
