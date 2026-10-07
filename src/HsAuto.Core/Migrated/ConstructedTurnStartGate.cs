// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Globalization;
using System.Linq;
using HsAuto.Core.Models;

namespace HsAuto.Core.Bot;
internal sealed class ConstructedTurnStartGate
{
    internal const int MinimumStableStateMs = 100;
    internal const int MaximumUnsignaledTurnStartWaitMs = 2500;
    private string _trackedTurnKey = "";
    private bool _released;
    private string _lastNonLocalHandSignature = "";
    private int _lastNonLocalHandCount;
    private bool _hasNonLocalHandSnapshot;
    private string _turnBaselineHandSignature = "";
    private int _turnBaselineHandCount;
    private bool _turnHasHandBaseline;
    private bool _sawTurnStartActivity;
    public ConstructedTurnStartGateCheck Check(ConstructedGameState state, bool enabled)
    {
        if (!enabled)
        {
            Reset();
            return ConstructedTurnStartGateCheck.NotApplicable;
        }

        if (state.Phase != ConstructedPhase.LocalTurn)
        {
            _lastNonLocalHandSignature = BuildHandSignature(state);
            _lastNonLocalHandCount = state.Hand.Count;
            _hasNonLocalHandSnapshot = true;
            return ConstructedTurnStartGateCheck.NotApplicable;
        }

        string text = BuildTurnKey(state);
        if (!string.Equals(_trackedTurnKey, text, StringComparison.Ordinal))
        {
            _trackedTurnKey = text;
            _released = false;
            _turnBaselineHandSignature = _lastNonLocalHandSignature;
            _turnBaselineHandCount = _lastNonLocalHandCount;
            _turnHasHandBaseline = _hasNonLocalHandSnapshot;
            _sawTurnStartActivity = false;
        }

        if (_released)
        {
            return ConstructedTurnStartGateCheck.Ready;
        }

        if (ConstructedHumanizedPacingScheduler.IsRopeActive(state))
        {
            _released = true;
            return new ConstructedTurnStartGateCheck(CanProceed: true, RopeBypass: true, "");
        }

        if (IsTagTrue(state, "unity.turnStart.blockingInput") || IsTagTrue(state, "unity.turnStart.listeningForTurnEvents") || IsTagTrue(state, "unity.turnStart.indicatorShowing") || IsTagTrue(state, "unity.turnTimer.waitingForTurnStartManager"))
        {
            _sawTurnStartActivity = true;
            return new ConstructedTurnStartGateCheck(CanProceed: false, RopeBypass: false, "等待回合开始动画和右侧抽牌完成");
        }

        int num = ReadTagInt(state, "constructed.phaseStableMs");
        if (num < 100 || ReadTagInt(state, "constructed.handStableMs") < 100)
        {
            return new ConstructedTurnStartGateCheck(CanProceed: false, RopeBypass: false, "等待抽牌后的我方回合与手牌快照稳定");
        }

        string b = BuildHandSignature(state);
        bool num2 = _turnHasHandBaseline && !string.Equals(_turnBaselineHandSignature, b, StringComparison.Ordinal);
        bool flag = _turnHasHandBaseline && _turnBaselineHandCount >= 10;
        bool flag2 = !_turnHasHandBaseline;
        bool flag3 = num >= 2500;
        if (!num2 && !_sawTurnStartActivity && !flag && !flag2 && !flag3)
        {
            return new ConstructedTurnStartGateCheck(CanProceed: false, RopeBypass: false, "等待右侧新抽取的手牌实体出现");
        }

        _released = true;
        return ConstructedTurnStartGateCheck.Ready;
    }

    private void Reset()
    {
        _trackedTurnKey = "";
        _released = false;
        _lastNonLocalHandSignature = "";
        _lastNonLocalHandCount = 0;
        _hasNonLocalHandSnapshot = false;
        _turnBaselineHandSignature = "";
        _turnBaselineHandCount = 0;
        _turnHasHandBaseline = false;
        _sawTurnStartActivity = false;
    }

    private static string BuildTurnKey(ConstructedGameState state)
    {
        return string.Join("|", state.MatchId, state.Turn.ToString(CultureInfo.InvariantCulture));
    }

    private static string BuildHandSignature(ConstructedGameState state)
    {
        return string.Join(",", from card in state.Hand
            select card.EntityId into entityId
                where !string.IsNullOrWhiteSpace(entityId)select entityId);
    }

    private static bool IsTagTrue(ConstructedGameState state, string key)
    {
        string value;
        bool result = default;
        return (state.Tags.TryGetValue(key, out value) && bool.TryParse(value, out result)) & result;
    }

    private static int ReadTagInt(ConstructedGameState state, string key)
    {
        if (!state.Tags.TryGetValue(key, out string value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }
}