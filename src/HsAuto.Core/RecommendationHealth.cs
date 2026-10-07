using HsAuto.Core.Models;
using HsAuto.Core.Automation;

namespace HsAuto.Open;

// Only diagnoses an absent actionable recommendation. Never fabricates a move or changes providers.
public sealed class RecommendationHealth
{
    readonly TimeProvider clock;
    readonly TimeSpan timeout;
    readonly TimeSpan executionTimeout;
    string scope = "";
    DateTimeOffset? missingSince;
    bool reported;
    string executionScope = "";
    DateTimeOffset? executionWaitingSince;
    public RecommendationHealth(TimeProvider? timeProvider = null, TimeSpan? waitingLimit = null, TimeSpan? executionWaitingLimit = null)
    {
        clock = timeProvider ?? TimeProvider.System;
        timeout = waitingLimit ?? TimeSpan.FromSeconds(30);
        executionTimeout = executionWaitingLimit ?? TimeSpan.FromSeconds(60);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(waitingLimit));
        if (executionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(executionWaitingLimit));
    }
    public void Reset()
    { scope = executionScope = ""; missingSince = executionWaitingSince = null; reported = false; }
    public string? ObserveExecution(ConstructedGameState state, ConstructedActionExecutionResult result)
    {
        var next = state.MatchId + "|" + state.Turn + "|" + state.Phase;
        if (executionScope != next || result.Attempted)
        { executionScope = next; executionWaitingSince = null; }
        if (result.Attempted) return null;
        executionWaitingSince ??= clock.GetUtcNow();
        if (clock.GetUtcNow() - executionWaitingSince.Value < executionTimeout) return null;
        return $"已收到盒子建议，但游戏操作阶段连续{executionTimeout.TotalSeconds:0}秒未就绪，正在自动重新连接。等待原因：" + result.Reason;
    }

    public string? Observe(ConstructedGameState state, IReadOnlyList<ConstructedAction> actions, bool readOnly)
    {
        if (state.Phase is not (ConstructedPhase.Mulligan or ConstructedPhase.LocalTurn or ConstructedPhase.Discover or ConstructedPhase.Choice))
        { scope = ""; missingSince = null; reported = false; return null; }
        var next = state.MatchId + "|" + state.Turn + "|" + state.Phase;
        if (scope != next) { scope = next; missingSince = null; reported = false; }
        if (actions.Any(a => a.Type is not (ConstructedActionType.Wait or ConstructedActionType.NoOp)))
        { missingSince = null; reported = false; return null; }
        missingSince ??= clock.GetUtcNow();
        if (clock.GetUtcNow() - missingSince.Value < timeout || reported) return null;
        reported = true;
        return $"Bridge 已连接，但盒子本机推荐源在本阶段等待 {timeout.TotalSeconds:0} 秒仍没有可用建议。" +
            (readOnly ? "只读观察继续，不提交游戏操作。" : "正在自动重新连接，不自动投降、不猜测出牌。") +
            "本版没有启用原版盒子AI服务直连；重新安装插件不能代替推荐源接入。";
    }
}
