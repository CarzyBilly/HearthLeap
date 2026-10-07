// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Globalization;
using System.Linq;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;

namespace HsAuto.Core.Bot;
internal sealed class ConstructedHumanizedPacingScheduler
{
    private sealed record PendingAction(string ContextKey, ConstructedDecisionEnvelope Decision, DateTimeOffset ExecuteNotBefore, int DelayMs);
    internal const int ContinuousActionMinimumDelayMs = 500;
    internal const int ContinuousActionMaximumDelayMs = 1200;
    internal const int ChoiceMinimumDelayMs = 500;
    internal const int ChoiceMaximumDelayMs = 1800;
    internal const int Ai2ChoiceMinimumDelayMs = 1200;
    internal const int Ai2ChoiceMaximumDelayMs = 2200;
    internal const int EndTurnMinimumDelayMs = 220;
    internal const int EndTurnMaximumDelayMs = 750;
    internal const int AcceleratedMinimumDelayMs = 80;
    internal const int AcceleratedMaximumDelayMs = 260;
    internal const int AcceleratedActionThreshold = 12;
    internal static readonly TimeSpan AcceleratedTurnAge = TimeSpan.FromSeconds(45.0);
    private readonly TimeProvider _timeProvider;
    private readonly Func<int, int, int>? _nextDelayMs;
    private readonly HumanizedDelayModel _delayModel;
    private readonly bool _useAi2ChoiceDelay;
    private PendingAction? _pending;
    private string _trackedTurnKey = "";
    private DateTimeOffset _trackedTurnStartedAt = DateTimeOffset.MinValue;
    private int _successfulActionCount;
    public ConstructedHumanizedPacingScheduler(TimeProvider? timeProvider = null, Func<int, int, int>? nextDelayMs = null, HumanizedDelayModel? delayModel = null, bool useAi2ChoiceDelay = false)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _nextDelayMs = nextDelayMs;
        _delayModel = delayModel ?? new HumanizedDelayModel();
        _useAi2ChoiceDelay = useAi2ChoiceDelay;
    }

    public HumanizedPendingCheck CheckPending(ConstructedGameState state, bool enabled)
    {
        if (!enabled)
        {
            bool flag = (object)_pending != null;
            _pending = null;
            if (!flag)
            {
                return HumanizedPendingCheck.None;
            }

            return HumanizedPendingCheck.Canceled("盒子AI拟人已关闭，立即恢复原执行路径");
        }

        if ((object)_pending == null)
        {
            return HumanizedPendingCheck.None;
        }

        string b = BuildContextKey(state);
        if (!string.Equals(_pending.ContextKey, b, StringComparison.Ordinal))
        {
            _pending = null;
            return HumanizedPendingCheck.Canceled("局面、回合或客户端选项已经变化，取消旧的拟人等待并重新获取推荐");
        }

        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        if (IsRopeActive(state) || utcNow >= _pending.ExecuteNotBefore)
        {
            PendingAction pending = _pending;
            _pending = null;
            return HumanizedPendingCheck.Ready(pending.Decision, pending.DelayMs, IsRopeActive(state));
        }

        return HumanizedPendingCheck.Waiting(_pending.Decision, _pending.DelayMs, _pending.ExecuteNotBefore - utcNow);
    }

    public HumanizedPacingSchedule? TrySchedule(ConstructedGameState state, ConstructedDecisionEnvelope decision, bool enabled)
    {
        if (!enabled || decision.DryRun || (object)_pending != null || IsRopeActive(state))
        {
            return null;
        }

        ConstructedAction constructedAction = decision.Actions.FirstOrDefault(IsPaceableAction);
        bool flag = (object)constructedAction == null;
        if (!flag)
        {
            ConstructedPhase phase = state.Phase;
            bool flag2 = ((phase == ConstructedPhase.LocalTurn || (uint)(phase - 5) <= 1u) ? true : false);
            flag = !flag2;
        }

        if (flag)
        {
            return null;
        }

        EnsureTrackedTurn(state);
        (int MinimumDelayMs, int MaximumDelayMs) delayRange = GetDelayRange(state, constructedAction);
        int item = delayRange.MinimumDelayMs;
        int item2 = delayRange.MaximumDelayMs;
        string turnKey = BuildTurnKey(state);
        HumanizedActionCategory category = GetCategory(state, constructedAction);
        int num = Math.Clamp((_nextDelayMs == null) ? _delayModel.NextDelayMs(category, turnKey, item, item2, category != HumanizedActionCategory.Accelerated) : _nextDelayMs(item, item2), item, item2);
        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        _pending = new PendingAction(BuildContextKey(state), decision, utcNow.AddMilliseconds(num), num);
        return new HumanizedPacingSchedule(constructedAction, num);
    }

    public void RecordSuccessfulAction(ConstructedGameState state, ConstructedAction action)
    {
        if (IsTrackedAction(action))
        {
            EnsureTrackedTurn(state);
            _successfulActionCount++;
        }
    }

    internal static bool IsRopeActive(ConstructedGameState state)
    {
        string value;
        bool result = default;
        return (state.Tags.TryGetValue("unity.turnTimer.ropeActive", out value) && bool.TryParse(value, out result)) & result;
    }

    internal static string BuildContextKey(ConstructedGameState state)
    {
        state.Tags.TryGetValue("unity.optionsPacketId", out string value);
        ConstructedPhase phase = state.Phase;
        bool flag = (uint)(phase - 5) <= 1u;
        string text = (flag ? string.Join(",", (
            from option in state.DiscoverOptions.Concat(state.ChoiceOptions)select option.EntityId).OrderBy((string entityId) => entityId, StringComparer.OrdinalIgnoreCase)) : "");
        string text2 = ((state.Phase == ConstructedPhase.LocalTurn) ? string.Join(",", from card in state.Hand
            select card.EntityId into entityId
                where !string.IsNullOrWhiteSpace(entityId)select entityId) : "");
        return string.Join("|", state.MatchId, state.Turn.ToString(CultureInfo.InvariantCulture), state.Phase, value ?? "0", text, text2);
    }

    private static bool IsPaceableAction(ConstructedAction action)
    {
        ConstructedActionType type = action.Type;
        if ((uint)(type - 5) <= 1u || (uint)(type - 8) <= 7u)
        {
            return true;
        }

        return false;
    }

    private static bool IsTrackedAction(ConstructedAction action)
    {
        if (!IsPaceableAction(action))
        {
            return action.Type == ConstructedActionType.Attack;
        }

        return true;
    }

    private (int MinimumDelayMs, int MaximumDelayMs) GetDelayRange(ConstructedGameState state, ConstructedAction action)
    {
        ConstructedPhase phase = state.Phase;
        bool flag = (uint)(phase - 5) <= 1u;
        bool flag2 = flag || action.Type == ConstructedActionType.ChooseOption;
        if (flag2 && _useAi2ChoiceDelay)
        {
            return (MinimumDelayMs: 1200, MaximumDelayMs: 2200);
        }

        if (IsContinuousAction(action))
        {
            return (MinimumDelayMs: 500, MaximumDelayMs: 1200);
        }

        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        if (_successfulActionCount >= 12 || utcNow - _trackedTurnStartedAt >= AcceleratedTurnAge)
        {
            return (MinimumDelayMs: 80, MaximumDelayMs: 260);
        }

        if (action.Type == ConstructedActionType.EndTurn)
        {
            return (MinimumDelayMs: 220, MaximumDelayMs: 750);
        }

        if (flag2)
        {
            return (MinimumDelayMs: 500, MaximumDelayMs: 1800);
        }

        return (MinimumDelayMs: 500, MaximumDelayMs: 1200);
    }

    private static bool IsContinuousAction(ConstructedAction action)
    {
        if (IsPaceableAction(action) && action.Type != ConstructedActionType.ChooseOption)
        {
            return action.Type != ConstructedActionType.EndTurn;
        }

        return false;
    }

    private void EnsureTrackedTurn(ConstructedGameState state)
    {
        string text = BuildTurnKey(state);
        if (!string.Equals(_trackedTurnKey, text, StringComparison.Ordinal))
        {
            _trackedTurnKey = text;
            _trackedTurnStartedAt = _timeProvider.GetUtcNow();
            _successfulActionCount = 0;
        }
    }

    private static string BuildTurnKey(ConstructedGameState state)
    {
        return string.Join("|", state.MatchId, state.Turn.ToString(CultureInfo.InvariantCulture), (state.Phase == ConstructedPhase.Mulligan) ? "mulligan" : "turn");
    }

    private HumanizedActionCategory GetCategory(ConstructedGameState state, ConstructedAction action)
    {
        ConstructedPhase phase = state.Phase;
        bool flag = (uint)(phase - 5) <= 1u;
        if ((flag || action.Type == ConstructedActionType.ChooseOption) && _useAi2ChoiceDelay)
        {
            return HumanizedActionCategory.Choice;
        }

        if (IsContinuousAction(action))
        {
            if (_successfulActionCount != 0)
            {
                return HumanizedActionCategory.OrdinaryAction;
            }

            return HumanizedActionCategory.FirstAction;
        }

        if (_successfulActionCount >= 12 || _timeProvider.GetUtcNow() - _trackedTurnStartedAt >= AcceleratedTurnAge)
        {
            return HumanizedActionCategory.Accelerated;
        }

        switch (action.Type)
        {
            case ConstructedActionType.ChooseOption:
                return HumanizedActionCategory.Choice;
            case ConstructedActionType.EndTurn:
                return HumanizedActionCategory.EndTurn;
            default:
                phase = state.Phase;
                if ((uint)(phase - 5) <= 1u)
                {
                    return HumanizedActionCategory.Choice;
                }

                if (_successfulActionCount == 0)
                {
                    return HumanizedActionCategory.FirstAction;
                }

                return HumanizedActionCategory.OrdinaryAction;
        }
    }
}