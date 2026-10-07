// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;

namespace HsAuto.Core.Bot;
internal sealed class NeteaseConstructedStallConcedeTracker
{
    internal const int ConsecutiveConcedeLimit = 3;
    internal const string ProtectionConcedeParameter = "hsauto.neteaseConstructedStallConcede";
    internal const string TurnParameter = "hsauto.neteaseStallConcedeTurn";
    internal const string MatchParameter = "hsauto.neteaseStallConcedeMatch";
    private int _consecutiveConcedes;
    private bool _stallConcedeRecordedForCurrentGame;
    private bool _enabled;
    private string? _localMatchId;
    private int _localTurn;
    private int _previousTurn;
    private ConstructedPhase _previousPhase;
    private bool _observedTurnStart;
    private bool _successfulTurnAction;
    private string? _executionFailure;
    internal int ConsecutiveConcedes => _consecutiveConcedes;

    internal void SetEnabled(bool enabled)
    {
        if (_enabled != enabled)
        {
            _enabled = enabled;
            if (!enabled)
            {
                Reset();
            }
        }
    }

    internal void ObserveState(ConstructedGameState state)
    {
        if (_enabled)
        {
            bool flag = state.Result != ConstructedGameResult.Unknown;
            if (!flag)
            {
                ConstructedPhase phase = state.Phase;
                bool flag2 = (((uint)(phase - 1) <= 1u || phase == ConstructedPhase.GameOver) ? true : false);
                flag = flag2;
            }

            if (flag)
            {
                ResetTurn();
            }
            else if (state.Phase == ConstructedPhase.LocalTurn && (_localMatchId != state.MatchId || _localTurn != state.Turn))
            {
                bool flag3 = (_previousPhase == ConstructedPhase.OpponentTurn && _previousTurn > 0 && state.Turn == _previousTurn + 1) || (_previousPhase == ConstructedPhase.Mulligan && state.Turn == 1);
                _localMatchId = state.MatchId;
                _localTurn = state.Turn;
                _observedTurnStart = flag3 && !ConstructedHumanizedPacingScheduler.IsRopeActive(state);
                _successfulTurnAction = false;
                _executionFailure = null;
            }

            _previousPhase = state.Phase;
            _previousTurn = state.Turn;
        }
    }

    internal void RecordExecution(ConstructedGameState state, ConstructedActionExecutionResult result)
    {
        bool flag = !_enabled || !result.Attempted || (object)result.Action == null || _localMatchId != state.MatchId || _localTurn != state.Turn;
        if (!flag)
        {
            ConstructedPhase phase = state.Phase;
            bool flag2 = ((phase == ConstructedPhase.LocalTurn || (uint)(phase - 5) <= 1u) ? true : false);
            flag = !flag2;
        }

        if (!flag && IsTurnAction(result.Action))
        {
            if (result.Succeeded)
            {
                _successfulTurnAction = true;
                _executionFailure = null;
            }
            else
            {
                _executionFailure = result.Reason;
            }
        }
    }

    internal void RecordExecutionException(ConstructedGameState state, ConstructedDecisionEnvelope decision, string reason)
    {
        if (_enabled && !decision.DryRun)
        {
            ConstructedAction constructedAction = decision.Actions.FirstOrDefault(IsTurnAction);
            if ((object)constructedAction != null)
            {
                RecordExecution(state, ConstructedActionExecutionResult.Failed(constructedAction, reason));
            }
        }
    }

    internal ConstructedDecisionEnvelope? TryCreateConcedeDecision(ConstructedGameState state, ConstructedDecisionEnvelope strategyDecision, bool supportedStrategy)
    {
        if (!_enabled || !supportedStrategy || strategyDecision.DryRun || state.Result != ConstructedGameResult.Unknown || state.Phase != ConstructedPhase.LocalTurn || _localMatchId != state.MatchId || _localTurn != state.Turn || (_executionFailure == null && (!_observedTurnStart || _successfulTurnAction)) || !ConstructedHumanizedPacingScheduler.IsRopeActive(state) || strategyDecision.Actions.Any((ConstructedAction action) => action.Type == ConstructedActionType.Concede) || (_executionFailure == null && strategyDecision.Actions.Any(IsExecutableAction)))
        {
            return null;
        }

        ConstructedAction constructedAction = strategyDecision.Actions.FirstOrDefault((ConstructedAction action) => action.Type == ConstructedActionType.Wait && action.Parameters.TryGetValue("hsauto.neteaseConstructedStallKind", out string value2) && !string.IsNullOrWhiteSpace(value2));
        if ((object)constructedAction == null && _executionFailure == null)
        {
            return null;
        }

        string value = null;
        constructedAction?.Parameters.TryGetValue("hsauto.neteaseConstructedStallKind", out value);
        ConstructedAction constructedAction2 = new ConstructedAction
        {
            Type = ConstructedActionType.Concede,
            Reason = ((_executionFailure != null) ? ("盒子AI推荐动作执行失败且未恢复，已烧绳，投降本局：" + _executionFailure) : ("盒子AI本回合从开始到烧绳始终无成功操作，投降本局：" + constructedAction.Reason)),
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hsauto.neteaseConstructedStallConcede"] = bool.TrueString,
                ["hsauto.neteaseStallConcedeTurn"] = state.Turn.ToString(CultureInfo.InvariantCulture),
                ["hsauto.neteaseStallConcedeMatch"] = state.MatchId,
                ["hsauto.neteaseConstructedStallKind"] = ((_executionFailure != null) ? "Execution" : (value ?? "Unknown"))
            }
        };
        return new ConstructedDecisionEnvelope
        {
            MatchId = state.MatchId,
            ObservedStateSequence = strategyDecision.ObservedStateSequence,
            StrategyName = strategyDecision.StrategyName,
            DryRun = false,
            Actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(constructedAction2),
            Summary = constructedAction2.ToString()
        };
    }

    internal NeteaseConstructedStallConcedeResult RecordSuccessfulConcede(ConstructedAction action)
    {
        if (!_enabled || !action.Parameters.TryGetValue("hsauto.neteaseConstructedStallConcede", out string value) || !bool.TryParse(value, out var result) || !result)
        {
            return new NeteaseConstructedStallConcedeResult(Recorded: false, _consecutiveConcedes, ShouldStop: false);
        }

        if (!_stallConcedeRecordedForCurrentGame)
        {
            _stallConcedeRecordedForCurrentGame = true;
            _consecutiveConcedes = Math.Min(3, _consecutiveConcedes + 1);
        }

        return new NeteaseConstructedStallConcedeResult(Recorded: true, _consecutiveConcedes, _consecutiveConcedes >= 3);
    }

    internal void ObserveGameCompleted()
    {
        if (_enabled)
        {
            if (!_stallConcedeRecordedForCurrentGame)
            {
                _consecutiveConcedes = 0;
            }

            _stallConcedeRecordedForCurrentGame = false;
            ResetTurn();
        }
    }

    private void Reset()
    {
        _consecutiveConcedes = 0;
        _stallConcedeRecordedForCurrentGame = false;
        ResetTurn();
    }

    private void ResetTurn()
    {
        _localMatchId = null;
        _localTurn = 0;
        _previousTurn = 0;
        _previousPhase = ConstructedPhase.Unknown;
        _observedTurnStart = false;
        _successfulTurnAction = false;
        _executionFailure = null;
    }

    internal static bool IsCurrentConcede(ConstructedGameState state, ConstructedAction action)
    {
        if (state.Result == ConstructedGameResult.Unknown && state.Phase == ConstructedPhase.LocalTurn && ConstructedHumanizedPacingScheduler.IsRopeActive(state) && action.Parameters.TryGetValue("hsauto.neteaseStallConcedeMatch", out string value) && value == state.MatchId && action.Parameters.TryGetValue("hsauto.neteaseStallConcedeTurn", out string value2) && int.TryParse(value2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return result == state.Turn;
        }

        return false;
    }

    private static bool IsTurnAction(ConstructedAction action)
    {
        bool flag = IsExecutableAction(action);
        if (flag)
        {
            ConstructedActionType type = action.Type;
            bool flag2 = (((uint)(type - 2) <= 2u || (uint)(type - 16) <= 1u) ? true : false);
            flag = !flag2;
        }

        return flag;
    }

    private static bool IsExecutableAction(ConstructedAction action)
    {
        ConstructedActionType type = action.Type;
        bool flag = (uint)type <= 1u;
        return !flag;
    }
}