// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Bot;
using HsAuto.Core.Models;
using HsAuto.Core.Strategy;

namespace HsAuto.Core.Automation;
public sealed class ConstructedActionExecutor
{
    private sealed record FailedActionState(int Count, DateTimeOffset LastFailedAt, DateTimeOffset SuppressedUntil, string Reason);
    private static readonly string[] CardClickComponents = new string[3]
    {
        "Card",
        "Clickable",
        "PegUIElement"
    };
    private static readonly string[] ControlClickComponents = new string[6]
    {
        "PegUIElement",
        "UIBButton",
        "NormalButton",
        "MulliganButton",
        "EndTurnButton",
        "Clickable"
    };
    private static readonly TimeSpan PredictedTargetDeathCooldown = TimeSpan.FromSeconds(8.0);
    private static readonly TimeSpan FailedActionSuppressionCooldown = TimeSpan.FromSeconds(8.0);
    private const int MaxRepeatedActionFailures = 2;
    private readonly UnityBridgeClient _client;
    private readonly UnityBridgeConstructedGameStateReader _reader;
    private readonly CardOperationPacing _pacing;
    private readonly HashSet<string> _selectedMulliganReplacementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly object _predictedDeadTargetsLock = new object ();
    private readonly Dictionary<string, DateTimeOffset> _predictedDeadTargets = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
    private readonly object _failedActionsLock = new object ();
    private readonly Dictionary<string, FailedActionState> _failedActions = new Dictionary<string, FailedActionState>(StringComparer.OrdinalIgnoreCase);
    private string _mulliganSelectionMatchId = "";
    private bool _mulliganConfirmed;
    private bool _choiceVisualConfirmationPending;
    private bool _choiceVisualConfirmationFastTiming;
    private bool _darkGiftSelectionDeferred;
    public bool AllowLegacyEndTurnObjectFallback { get; set; } = true;

    public event Action<string>? Log;
    public ConstructedActionExecutor(UnityBridgeClient client, UnityBridgeConstructedGameStateReader reader, CardOperationPacing pacing = null)
    {
        _client = client;
        _reader = reader;
        _pacing = pacing ?? new CardOperationPacing();
    }

    public void ObservePhase(ConstructedPhase phase)
    {
        // A transient unclassified frame is not an exit from the mulligan.
        if (phase is ConstructedPhase.LocalTurn or ConstructedPhase.OpponentTurn or
            ConstructedPhase.Discover or ConstructedPhase.Choice or ConstructedPhase.Queue or ConstructedPhase.GameOver)
        {
            ResetMulliganSelectionTracking();
        }
    }

    public async Task<ConstructedActionExecutionResult> ExecuteAsync(ConstructedDecisionEnvelope decision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (decision.DryRun)
        {
            return ConstructedActionExecutionResult.NoAction("只读观察不提交游戏操作");
        }

        if (decision.Actions.Any((ConstructedAction constructedAction) =>
        {
            ConstructedActionType type = constructedAction.Type;
            return (uint)(type - 3) <= 1u;
        }))
        {
            return await ExecuteMulliganAsync(decision, cancellationToken);
        }

        if (_choiceVisualConfirmationPending)
        {
            if (!(await ConfirmFriendlyChoiceDismissedAsync(allowCleanup: true, _choiceVisualConfirmationFastTiming, cancellationToken)))
            {
                return ConstructedActionExecutionResult.NoAction("发现选择已提交，但候选卡尚未全部退出界面，禁止继续后续动作");
            }

            _choiceVisualConfirmationPending = false;
            _choiceVisualConfirmationFastTiming = false;
            LogMessage("发现选择候选卡已全部退出界面，允许继续执行盒子下一手推荐");
        }

        ConstructedAction[] executableActions = decision.Actions.Where(IsExecutableAction).ToArray();
        if (executableActions.Length == 0)
        {
            return ConstructedActionExecutionResult.NoAction("没有可执行动作");
        }

        UnityBridgeConstructedObservedState observed = await _reader.ReadAsync(cancellationToken);
        if (!CardOperationPacing.MatchesDecision(decision, observed.State))
        {
            return ConstructedActionExecutionResult.NoAction("局面已更新，等待重新获取建议");
        }
        ConstructedAction action = executableActions.FirstOrDefault((ConstructedAction candidate) => IsNeteaseAction(candidate) || !IsActionTemporarilySuppressed(observed.State, candidate, out string _));
        if ((object)action == null)
        {
            return ConstructedActionExecutionResult.NoAction("连续失败的传统动作已被临时跳过，等待重新决策");
        }

        if (!IsNeteaseAction(action) && IsPredictedDeadTargetAction(observed.State, action, out string reason))
        {
            LogMessage(reason);
            return ConstructedActionExecutionResult.NoAction(reason);
        }

        if (!TryValidateAndBind(observed.State, action, out ConstructedAction boundAction, out string reason2))
        {
            LogMessage("动作二次校验失败：" + reason2);
            if (action.Type == ConstructedActionType.Concede && (action.Parameters.ContainsKey("hsauto.ai2WinRateState") || action.Parameters.ContainsKey("hsauto.neteaseConstructedStallConcede")))
            {
                return ConstructedActionExecutionResult.NoAction(reason2);
            }

            if (!IsNeteaseAction(action) && RecordActionFailure(observed.State, action, reason2, out string suppressionReason))
            {
                LogMessage(suppressionReason);
                await TryCancelPendingInputAsync("同一传统动作连续校验失败，临时取消该源/目标操作", cancellationToken);
                return ConstructedActionExecutionResult.Failed(action, suppressionReason);
            }

            return ConstructedActionExecutionResult.Failed(action, reason2);
        }

        if (await _pacing.WaitAsync(boundAction.Type, cancellationToken))
        {
            var beforeWait = observed.State;
            observed = await _reader.ReadAsync(cancellationToken);
            if (!CardOperationPacing.CanReuseAfterWait(beforeWait, observed.State, action) ||
                !TryValidateAndBind(observed.State, action, out boundAction, out _))
            {
                const string changed = "等待期间游戏状态已更新，正在重新获取建议。";
                LogMessage(changed);
                return ConstructedActionExecutionResult.NoAction(changed);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        _darkGiftSelectionDeferred = false;
        bool executed = await ExecuteActionAsync(boundAction, cancellationToken);
        if (_darkGiftSelectionDeferred)
        {
            return ConstructedActionExecutionResult.NoAction("黑暗发现候选尚未全部展示，等待最新局面");
        }

        if (executed)
        {
            ClearActionFailure(observed.State, boundAction);
            RememberPredictedTargetDeath(observed.State, boundAction);
        }
        else
        {
            if (boundAction.Type == ConstructedActionType.EndTurn && !AllowLegacyEndTurnObjectFallback)
            {
                return ConstructedActionExecutionResult.NoAction("传统基础结束回合暂不可用，等待下一份稳定局面");
            }

            if (!IsNeteaseAction(boundAction) && RecordActionFailure(observed.State, boundAction, "动作未确认成功", out string suppressionReason2))
            {
                LogMessage(suppressionReason2);
                await TryCancelPendingInputAsync("同一传统动作连续执行失败，临时取消该源/目标操作", cancellationToken);
            }
        }

        return executed ? ConstructedActionExecutionResult.Success(boundAction) : ConstructedActionExecutionResult.Failed(boundAction, "动作未确认成功");
    }

    private async Task<ConstructedActionExecutionResult> ExecuteMulliganAsync(ConstructedDecisionEnvelope decision, CancellationToken cancellationToken)
    {
        UnityBridgeConstructedObservedState observed = await _reader.ReadAsync(cancellationToken);
        if (!CardOperationPacing.MatchesDecision(decision, observed.State))
        {
            return ConstructedActionExecutionResult.NoAction("起手局面已更新，等待重新获取建议");
        }
        if (observed.State.Phase != ConstructedPhase.Mulligan)
        {
            ResetMulliganSelectionTracking();
            return ConstructedActionExecutionResult.NoAction($"当前阶段 {observed.State.Phase} 已不是起手换牌");
        }

        bool fastTiming = decision.Actions.Any(IsAi2FastTimingAction);
        if (!IsMulliganStableForExecution(observed.State, fastTiming))
        {
            return ConstructedActionExecutionResult.NoAction("起手换牌状态尚未稳定，等待下一帧再操作");
        }

        if (!string.Equals(_mulliganSelectionMatchId, observed.State.MatchId, StringComparison.Ordinal))
        {
            ResetMulliganSelectionTracking();
            _mulliganSelectionMatchId = observed.State.MatchId;
        }

        if (_mulliganConfirmed)
        {
            return ConstructedActionExecutionResult.NoAction("起手换牌已提交，等待服务器发牌并进入首回合");
        }

        int replaced = 0;
        foreach (ConstructedAction item in decision.Actions.Where((ConstructedAction constructedAction) => constructedAction.Type == ConstructedActionType.MulliganReplace))
        {
            if (!string.IsNullOrWhiteSpace(item.SourceEntityId) && _selectedMulliganReplacementIds.Contains(item.SourceEntityId))
            {
                continue;
            }

            if (!TryValidateAndBind(observed.State, item, out ConstructedAction boundAction, out string reason))
            {
                LogMessage("起手替换校验失败：" + reason);
                continue;
            }

            var beforeWait = observed.State;
            await _pacing.WaitAsync(item.Type, cancellationToken);
            observed = await _reader.ReadAsync(cancellationToken);
            if (!CardOperationPacing.CanReuseAfterWait(beforeWait, observed.State, item) ||
                !IsMulliganStableForExecution(observed.State, fastTiming) ||
                !TryValidateAndBind(observed.State, item, out boundAction, out _))
            {
                return ConstructedActionExecutionResult.NoAction("等待期间起手状态已更新，重新读取后再继续换牌");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (await TrySelectMulliganReplacementAsync(boundAction, cancellationToken))
            {
                replaced++;
                if (!string.IsNullOrWhiteSpace(boundAction.SourceEntityId))
                {
                    _selectedMulliganReplacementIds.Add(boundAction.SourceEntityId);
                }

                await Task.Delay(fastTiming ? 80 : 180, cancellationToken);
            }

            boundAction = null;
        }

        if (!decision.Actions.Any((ConstructedAction constructedAction) => constructedAction.Type == ConstructedActionType.ConfirmMulligan))
        {
            return (replaced > 0) ? ConstructedActionExecutionResult.Success(decision.Actions.First((ConstructedAction constructedAction) => constructedAction.Type == ConstructedActionType.MulliganReplace)) : ConstructedActionExecutionResult.NoAction("没有需要替换或确认的起手动作");
        }

        ConstructedAction action = decision.Actions.First((ConstructedAction constructedAction) => constructedAction.Type == ConstructedActionType.ConfirmMulligan);
        // The last replacement can complete while its animation is being awaited.
        observed = await _reader.ReadAsync(cancellationToken);
        if (!CardOperationPacing.MatchesDecision(decision, observed.State) || observed.State.Phase != ConstructedPhase.Mulligan)
        {
            return ConstructedActionExecutionResult.NoAction("起手阶段已结束，不再提交旧换牌操作");
        }
        if (!TryValidateAndBind(observed.State, action, out ConstructedAction boundConfirm, out string reason2))
        {
            LogMessage("确认起手校验失败：" + reason2);
            return ConstructedActionExecutionResult.Failed(action, reason2);
        }

        IReadOnlyList<int> keptEntityIds = (fastTiming ? MulliganKeptEntityIds(observed.State, decision.Actions) : null);
        bool flag = await TryConfirmMulliganAsync(keptEntityIds, cancellationToken);
        if (flag)
        {
            _mulliganConfirmed = true;
            if (keptEntityIds != null && boundConfirm.Parameters.ContainsKey("hsauto.ai3.auditMulligan"))
            {
                boundConfirm = boundConfirm with
                {
                    Parameters = new Dictionary<string, string>(boundConfirm.Parameters)
                    {
                        ["hsauto.ai3.submittedMulliganKeeps"] = string.Join(',', keptEntityIds)
                    }
                };
            }
        }

        return flag ? ConstructedActionExecutionResult.Success(boundConfirm) : ConstructedActionExecutionResult.Failed(boundConfirm, (replaced > 0) ? $"已选择 {replaced} 张替换牌，但确认按钮未点击成功" : "确认按钮未点击成功");
    }

    private void ResetMulliganSelectionTracking()
    {
        _selectedMulliganReplacementIds.Clear();
        _mulliganSelectionMatchId = "";
        _mulliganConfirmed = false;
    }

    private bool IsPredictedDeadTargetAction(ConstructedGameState state, ConstructedAction action, out string reason)
    {
        reason = "";
        ConstructedEntity constructedEntity = FindAnyEntityById(state, action.TargetEntityId);
        if ((object)constructedEntity == null || constructedEntity.Zone != ConstructedZone.EnemyBoard || string.IsNullOrWhiteSpace(constructedEntity.EntityId))
        {
            return false;
        }

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        lock (_predictedDeadTargetsLock)
        {
            PrunePredictedDeadTargets(utcNow);
            if (!_predictedDeadTargets.TryGetValue(PredictedDeadTargetKey(state, constructedEntity.EntityId), out var value) || value <= utcNow)
            {
                return false;
            }
        }

        reason = "目标 " + constructedEntity.Name + " 已被上一动作预计击杀，等待客户端场面刷新后重新决策";
        return true;
    }

    private void RememberPredictedTargetDeath(ConstructedGameState state, ConstructedAction action)
    {
        if (string.IsNullOrWhiteSpace(action.TargetEntityId))
        {
            return;
        }

        ConstructedEntity constructedEntity = FindAnyEntityById(state, action.TargetEntityId);
        if ((object)constructedEntity == null || constructedEntity.Zone != ConstructedZone.EnemyBoard || constructedEntity.HasTaunt)
        {
            return;
        }

        ConstructedEntity constructedEntity2 = FindAnyEntityById(state, action.SourceEntityId);
        if ((object)constructedEntity2 == null || !IsExpectedToDestroyTarget(action, constructedEntity2, constructedEntity))
        {
            return;
        }

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        lock (_predictedDeadTargetsLock)
        {
            PrunePredictedDeadTargets(utcNow);
            _predictedDeadTargets[PredictedDeadTargetKey(state, constructedEntity.EntityId)] = utcNow.Add(PredictedTargetDeathCooldown);
        }
    }

    private void PrunePredictedDeadTargets(DateTimeOffset now)
    {
        string[] array = (
            from item in _predictedDeadTargets
            where item.Value <= now
            select item.Key).ToArray();
        foreach (string key in array)
        {
            _predictedDeadTargets.Remove(key);
        }
    }

    private bool IsActionTemporarilySuppressed(ConstructedGameState state, ConstructedAction action, out string reason)
    {
        reason = "";
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        lock (_failedActionsLock)
        {
            PruneFailedActions(utcNow);
            string key = FailedActionKey(state, action);
            if (!_failedActions.TryGetValue(key, out FailedActionState value) || value.SuppressedUntil <= utcNow)
            {
                return false;
            }

            reason = value.Reason;
            return true;
        }
    }

    private bool RecordActionFailure(ConstructedGameState state, ConstructedAction action, string failureReason, out string suppressionReason)
    {
        suppressionReason = "";
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        string key = FailedActionKey(state, action);
        lock (_failedActionsLock)
        {
            PruneFailedActions(utcNow);
            _failedActions.TryGetValue(key, out FailedActionState value);
            int num = (((object)value == null || value.LastFailedAt == DateTimeOffset.MinValue || utcNow - value.LastFailedAt > FailedActionSuppressionCooldown) ? 1 : (value.Count + 1));
            if (num < 2)
            {
                _failedActions[key] = new FailedActionState(num, utcNow, DateTimeOffset.MinValue, failureReason);
                return false;
            }

            suppressionReason = $"同一传统动作连续失败 {num} 次，已临时跳过 {ActionLabel(action)}，原因：{failureReason}";
            _failedActions[key] = new FailedActionState(num, utcNow, utcNow.Add(FailedActionSuppressionCooldown), suppressionReason);
            return true;
        }
    }

    private void ClearActionFailure(ConstructedGameState state, ConstructedAction action)
    {
        lock (_failedActionsLock)
        {
            _failedActions.Remove(FailedActionKey(state, action));
        }
    }

    private void PruneFailedActions(DateTimeOffset now)
    {
        string[] array = (
            from item in _failedActions
            where (item.Value.SuppressedUntil != DateTimeOffset.MinValue && item.Value.SuppressedUntil <= now) || (item.Value.SuppressedUntil == DateTimeOffset.MinValue && now - item.Value.LastFailedAt > FailedActionSuppressionCooldown)select item.Key).ToArray();
        foreach (string key in array)
        {
            _failedActions.Remove(key);
        }
    }

    private static string FailedActionKey(ConstructedGameState state, ConstructedAction action)
    {
        return string.Join("|", state.MatchId, state.Turn, action.Type, action.SourceEntityId ?? "", action.TargetEntityId ?? "", action.OptionIndex?.ToString() ?? "");
    }

    private static bool IsExpectedToDestroyTarget(ConstructedAction action, ConstructedEntity source, ConstructedEntity target)
    {
        return action.Type switch
        {
            ConstructedActionType.Attack => CanKillDefender(source, target),
            ConstructedActionType.PlayCardWithTarget => EstimateDirectDamage(source) > 0 && CanSpellKill(EstimateDirectDamage(source), target),
            ConstructedActionType.UseHeroPowerWithTarget => source.IsHeroPower && target.Health <= 1 && !target.HasImmune && !target.HasDivineShield,
            _ => false,
        };
    }

    private static bool CanKillDefender(ConstructedEntity attacker, ConstructedEntity defender)
    {
        if (attacker.Attack > 0 && !defender.HasImmune && !defender.HasDivineShield)
        {
            return attacker.Attack >= EffectiveCombatHealth(defender);
        }

        return false;
    }

    private static bool CanSpellKill(int damage, ConstructedEntity target)
    {
        if (damage > 0 && !target.HasImmune && !target.HasDivineShield)
        {
            return damage >= EffectiveCombatHealth(target);
        }

        return false;
    }

    private static int EffectiveCombatHealth(ConstructedEntity entity)
    {
        return Math.Max(1, entity.Health) + (entity.HasDivineShield ? 1 : 0);
    }

    private static int EstimateDirectDamage(ConstructedEntity card)
    {
        if (card.Tags.TryGetValue("damage", out string value) && int.TryParse(value, out var result) && result > 0)
        {
            return result;
        }

        string text = $"{card.Name} {card.CardId} {ReadTag(card, "unity.cardText")}";
        int[] array = new int[10]
        {
            10,
            9,
            8,
            7,
            6,
            5,
            4,
            3,
            2,
            1
        };
        foreach (int num in array)
        {
            if (text.Contains($"造成{num}点", StringComparison.OrdinalIgnoreCase) || text.Contains($"Deal {num}", StringComparison.OrdinalIgnoreCase) || text.Contains($"deals {num}", StringComparison.OrdinalIgnoreCase))
            {
                return num;
            }
        }

        return 0;
    }

    private static string ReadTag(ConstructedEntity entity, string key)
    {
        if (!entity.Tags.TryGetValue(key, out string value))
        {
            return "";
        }

        return value;
    }

    private static string PredictedDeadTargetKey(ConstructedGameState state, string entityId)
    {
        return state.MatchId + "|" + entityId;
    }

    internal static bool IsMulliganStableForExecution(ConstructedGameState state, bool fastTiming)
    {
        if (state.Hand.Count <= 0 || IsTagTrue(state, "unity.hasMatchingPopup"))
        {
            return false;
        }

        if (fastTiming)
        {
            if (state.Phase != ConstructedPhase.Mulligan || !HasCompleteAi2MulliganHand(state))
            {
                return false;
            }

            if (IsTagTrue(state, "unity.hasMulliganConfirmButton") && IsTagTrue(state, "unity.isMulliganWaitingForUserInput") && IsAi2MulliganAnimationStable(state) && ReadTagInt(state, "constructed.phaseStableMs") >= 900)
            {
                return ReadTagInt(state, "constructed.handStableMs") >= 900;
            }

            return false;
        }

        if ((!state.Tags.ContainsKey("unity.hasMulliganConfirmButton") || IsTagTrue(state, "unity.hasMulliganConfirmButton")) && (!state.Tags.ContainsKey("unity.isMulliganWaitingForUserInput") || IsTagTrue(state, "unity.isMulliganWaitingForUserInput")) && ReadTagInt(state, "constructed.phaseStableMs") >= 800)
        {
            return ReadTagInt(state, "constructed.handStableMs") >= 800;
        }

        return false;
    }

    private static bool IsAi2MulliganAnimationStable(ConstructedGameState state)
    {
        if (!IsTagTrue(state, "unity.mulliganCardsTransitioning") && !IsTagTrue(state, "unity.mulliganIntroRunning"))
        {
            if (IsTagTrue(state, "unity.mulliganIntroStateAvailable"))
            {
                return IsTagTrue(state, "unity.mulliganIntroComplete");
            }

            return true;
        }

        return false;
    }

    private static bool HasCompleteAi2MulliganHand(ConstructedGameState state)
    {
        int count = state.Hand.Count;
        bool flag = ((count == 3 || count == 5) ? true : false);
        if (!flag || state.Hand.Any((ConstructedEntity card) => string.IsNullOrWhiteSpace(card.EntityId) || string.IsNullOrWhiteSpace(card.CardId) || card.CardId.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        int num = state.Hand.Count(ConstructedMulliganCardIdentity.IsCoin);
        if (state.Hand.Count != 3)
        {
            return num == 1;
        }

        return num == 0;
    }

    private static bool IsAi2FastTimingAction(ConstructedAction action)
    {
        if (action.Parameters.TryGetValue("hsauto.ai2.fastTiming", out string value))
        {
            return value == "1";
        }

        return false;
    }

    private static bool IsJail319Reroll(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.Parameters.TryGetValue("hsauto.jail319Reroll", out value) && bool.TryParse(value, out result)) & result;
    }

    public static bool TryValidateAndBind(ConstructedGameState state, ConstructedAction action, out ConstructedAction boundAction, out string reason)
    {
        boundAction = action;
        reason = "";
        if (action.Type == ConstructedActionType.Concede && action.Parameters.TryGetValue("hsauto.ai2WinRateState", out string value) && (state.Phase != ConstructedPhase.LocalTurn || value != NeteaseDirectTurnWinRateCheck.StateKey(state)))
        {
            reason = "盒子AI 胜率判断后局面、回合或选项已改变，取消过期投降";
            return false;
        }

        if (action.Type == ConstructedActionType.Concede && action.Parameters.ContainsKey("hsauto.neteaseConstructedStallConcede") && !NeteaseConstructedStallConcedeTracker.IsCurrentConcede(state, action))
        {
            reason = "卡住烧绳投降保护的回合、阶段或绳子状态已改变，取消过期投降";
            return false;
        }

        if (!IsExecutableAction(action))
        {
            return true;
        }

        if (!IsActionAllowedInPhase(state.Phase, action))
        {
            reason = "当前阶段" + PhaseLabel(state.Phase) + "不允许执行" + ActionTypeLabel(action.Type);
            return false;
        }

        Dictionary<string, string> dictionary = new Dictionary<string, string>(action.Parameters, StringComparer.OrdinalIgnoreCase);
        ConstructedEntity constructedEntity = null;
        ConstructedEntity constructedEntity2 = null;
        if (RequiresSource(action.Type) && !IsJail319Reroll(action))
        {
            constructedEntity = FindAnyEntityById(state, action.SourceEntityId);
            if ((object)constructedEntity == null)
            {
                reason = "动作源对象已不存在";
                return false;
            }

            CopyEntityTags(dictionary, constructedEntity);
        }

        IReadOnlyList<string> readOnlyList = ReadEntityIdListParameter(action);
        if (readOnlyList.Count > 0)
        {
            IEnumerable<ConstructedEntity> source = ((action.Type == ConstructedActionType.ChooseOption) ? state.ChoiceOptions.Concat(state.DiscoverOptions) : state.EnemyBoard.Concat(state.FriendlyBoard).Concat(state.Hand));
            HashSet<string> legalMultiTargetIds = source.Select((ConstructedEntity entity) => entity.EntityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string text = readOnlyList.FirstOrDefault((string entityId) => !legalMultiTargetIds.Contains(entityId));
            if (!string.IsNullOrWhiteSpace(text))
            {
                reason = "多选动作目标实体 " + text + " 已不存在或不在当前候选中";
                return false;
            }
        }

        if (RequiresTarget(action.Type))
        {
            constructedEntity2 = FindAnyEntityById(state, action.TargetEntityId);
            if ((object)constructedEntity2 == null)
            {
                reason = "动作目标对象已不存在";
                return false;
            }

            ApplyTargetParameters(dictionary, constructedEntity2);
        }

        if ((!HasInternalOption(action) || !HasMatchingPlayableAction(state, action)) && !ConstructedActionRules.IsTargetAllowed(state, action.Type, constructedEntity, constructedEntity2, out reason))
        {
            return false;
        }

        if (!IsStillLegal(state, action, constructedEntity, constructedEntity2, out reason))
        {
            return false;
        }

        ConstructedAction constructedAction = state.CurrentPlayableActions.FirstOrDefault((ConstructedAction candidate) => candidate.Type == action.Type && string.Equals(candidate.SourceEntityId, action.SourceEntityId, StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(action.TargetEntityId) || string.Equals(candidate.TargetEntityId, action.TargetEntityId, StringComparison.OrdinalIgnoreCase)) && (action.Type != ConstructedActionType.InternalOption || !action.OptionIndex.HasValue || candidate.OptionIndex == action.OptionIndex));
        if ((object)constructedAction != null)
        {
            foreach (KeyValuePair<string, string> parameter in constructedAction.Parameters)
            {
                dictionary[parameter.Key] = parameter.Value;
            }
        }

        boundAction = action with
        {
            OptionIndex = (constructedAction?.OptionIndex ?? action.OptionIndex),
            Parameters = dictionary
        };
        return true;
    }

    private async Task<bool> ExecuteActionAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        switch (action.Type)
        {
            case ConstructedActionType.MulliganKeep:
                LogMessage("保留起手牌：" + action.SourceEntityId);
                return true;
            case ConstructedActionType.MulliganReplace:
                return await TrySelectMulliganReplacementAsync(action, cancellationToken);
            case ConstructedActionType.ConfirmMulligan:
                return await TryConfirmMulliganAsync(null, cancellationToken);
            case ConstructedActionType.PlayCard:
            {
                bool flag;
                if (IsNeteaseSourceSelection(action))
                {
                    flag = await TryNetworkResponseAsync(action, preferCardSelector: true, cancellationToken);
                    if (!flag)
                    {
                        flag = await ClickSemanticObjectAsync(action, "选择盒子推荐的目标牌源", CardClickComponents, cancellationToken);
                    }

                    return flag;
                }

                if (HasInternalOption(action))
                {
                    return await TryConstructedOptionActionAsync(action, cancellationToken);
                }

                flag = await TryNetworkResponseAsync(action, preferCardSelector: true, cancellationToken);
                if (!flag)
                {
                    flag = await ClickSemanticObjectAsync(action, "打出手牌", CardClickComponents, cancellationToken);
                }

                return flag;
            }

            case ConstructedActionType.PlayCardWithTarget:
                return await TrySourceThenTargetAsync(action, cancellationToken);
            case ConstructedActionType.Attack:
                return await TrySourceThenTargetAsync(action, cancellationToken);
            case ConstructedActionType.UseHeroPower:
            {
                if (HasInternalOption(action))
                {
                    return await TryConstructedOptionActionAsync(action, cancellationToken);
                }

                bool flag = await TryNetworkResponseAsync(action, preferCardSelector: false, cancellationToken);
                if (!flag)
                {
                    flag = await ClickSemanticObjectAsync(action, "使用英雄技能", ControlClickComponents, cancellationToken);
                }

                return flag;
            }

            case ConstructedActionType.UseHeroPowerWithTarget:
                return await TrySourceThenTargetAsync(action, cancellationToken);
            case ConstructedActionType.UseLocation:
            case ConstructedActionType.UseLocationWithTarget:
            case ConstructedActionType.TradeCard:
            case ConstructedActionType.InternalOption:
                return await TryConstructedOptionActionAsync(action, cancellationToken);
            case ConstructedActionType.ChooseOption:
                return await TryChooseOptionAsync(action, cancellationToken);
            case ConstructedActionType.EndTurn:
                return await TryEndTurnAsync(cancellationToken);
            case ConstructedActionType.CancelPendingInput:
                return await TryCancelPendingInputAsync("策略检测到传统对战输入状态长时间未恢复", cancellationToken);
            case ConstructedActionType.Concede:
                return await TryConcedeAsync(cancellationToken);
            case ConstructedActionType.NoOp:
            case ConstructedActionType.Wait:
                LogMessage("跳过" + ActionTypeLabel(action.Type) + "：" + action.Reason);
                return false;
            default:
                LogMessage($"未知传统动作 {action.Type}");
                return false;
        }
    }

    private static bool HasInternalOption(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.OptionIndex.HasValue && action.Parameters.TryGetValue("unity.internalOption", out value) && bool.TryParse(value, out result)) & result;
    }

    private async Task<bool> TrySourceThenTargetAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        if (HasInternalOption(action))
        {
            return await TryConstructedOptionActionAsync(action, cancellationToken);
        }

        if (int.TryParse(action.SourceEntityId, out var sourceEntityId) && int.TryParse(action.TargetEntityId, out var targetEntityId) && sourceEntityId > 0 && targetEntityId > 0)
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("constructedTargetedAction", new { sourceEntityId = sourceEntityId, targetEntityId = targetEntityId, semanticAction = action.Type.ToString() }, 10000, cancellationToken);
            if (IsAccepted(bridgeResponse))
            {
                LogMessage($"{ActionLabel(action)} 已通过 GameState 选项 {sourceEntityId} -> {targetEntityId} 一次性提交");
                return true;
            }

            if (!IsUnknownCommand(bridgeResponse))
            {
                LogMessage(bridgeResponse.Ok ? (ActionLabel(action) + " 的目标已不在客户端有效选项中，等待重新读取局面") : (ActionLabel(action) + " 内部目标提交未接受：" + bridgeResponse.Error));
                await TryCancelPendingInputAsync("目标选项提交未接受", cancellationToken);
                return false;
            }
        }

        object obj = BuildNetworkResponseSelector(action, preferCardSelector: true);
        object targetSelector = BuildTargetSelector(action);
        if (obj == null || targetSelector == null)
        {
            return false;
        }

        BridgeResponse bridgeResponse2 = await SendNetworkResponseAsync(obj, checkValidInput: true, cancellationToken);
        if (!IsAccepted(bridgeResponse2))
        {
            if (!bridgeResponse2.Ok)
            {
                LogMessage("源对象内部响应失败：" + bridgeResponse2.Error);
            }

            return false;
        }

        await Task.Delay(IsAi2FastTimingAction(action) ? 60 : 180, cancellationToken);
        if (IsAccepted(await SendNetworkResponseAsync(targetSelector, checkValidInput: true, cancellationToken)))
        {
            LogMessage(ActionLabel(action) + " 已通过源对象 + 目标对象内部响应执行");
            return true;
        }

        LogMessage(ActionLabel(action) + " 目标内部响应未接受，清理已选中的源对象并等待重新读取状态");
        await TryCancelPendingInputAsync("源对象已选中但目标响应未接受", cancellationToken);
        return false;
    }

    private async Task<bool> TryConstructedOptionActionAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        if (!int.TryParse(action.SourceEntityId, out var result) || result <= 0 || !action.OptionIndex.HasValue)
        {
            LogMessage(ActionLabel(action) + " 缺少内部源实体或 optionIndex");
            return false;
        }

        int.TryParse(action.TargetEntityId, out var result2);
        int[] targetEntityIds = (
            from entityId in ReadEntityIdListParameter(action)select int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3) ? result3 : 0 into entityId
                where entityId > 0
                select entityId).ToArray();
        int num = ReadIntParameter(action, "unity.selectedSubOptionIndex", -1);
        string subOptionCardId = (action.Parameters.TryGetValue("netease.subOptionCardId", out string value) ? value : "");
        int subOptionEntityId = ((num >= 0) ? ReadIntParameter(action, $"unity.subOption.{num}.entityId", 0) : 0);
        int subOptionPosition = ((num >= 0) ? ReadIntParameter(action, $"unity.subOption.{num}.zonePosition", 0) : 0);
        int boardPosition = ReadIntParameter(action, "netease.boardPosition", 0);
        BridgeResponse bridgeResponse = await _client.SendAsync("constructedOptionAction", new { sourceEntityId = result, targetEntityId = result2, targetEntityIds = targetEntityIds, optionIndex = action.OptionIndex.Value, subOptionIndex = num, subOptionEntityId = subOptionEntityId, subOptionCardId = subOptionCardId, subOptionPosition = subOptionPosition, boardPosition = boardPosition, semanticAction = action.Type.ToString() }, 10000, cancellationToken);
        if (IsAccepted(bridgeResponse))
        {
            LogMessage(string.IsNullOrWhiteSpace(subOptionCardId) ? (ActionLabel(action) + " 已通过内部 OptionsPacket 提交") : (ActionLabel(action) + " 已按子选项 " + subOptionCardId + " 通过内部 OptionsPacket 直接提交"));
            return true;
        }

        LogMessage(bridgeResponse.Ok ? (ActionLabel(action) + " 的内部选项未被接受") : (ActionLabel(action) + " 内部提交失败：" + bridgeResponse.Error));
        return false;
    }

    private static int ReadIntParameter(ConstructedAction action, string key, int fallback)
    {
        if (!action.Parameters.TryGetValue(key, out string value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return fallback;
        }

        return result;
    }

    private static IReadOnlyList<string> ReadEntityIdListParameter(ConstructedAction action)
    {
        if (!action.Parameters.TryGetValue("netease.targetEntityIds", out string value) || string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return (
            from entityId in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            where int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
            select entityId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<bool> TryCancelPendingInputAsync(string reason, CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("constructedCancelInput", new { }, 10000, cancellationToken);
        if (IsAccepted(bridgeResponse))
        {
            bool flag = bridgeResponse.Data.TryGetProperty("cancelled", out var value) && value.ValueKind == JsonValueKind.True;
            LogMessage(flag ? ("已通过 InputManager.CancelOption 清理传统对战待定输入：" + reason) : ("传统对战当前没有待取消输入：" + reason));
            return true;
        }

        if (!IsUnknownCommand(bridgeResponse))
        {
            LogMessage("清理传统对战待定输入失败：" + bridgeResponse.Error);
        }

        return false;
    }

    private async Task<bool> TryChooseOptionAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        if (action.OptionIndex.HasValue)
        {
            bool result = default;
            bool useJail319RerollFlow = (action.Parameters.TryGetValue("hsauto.jail319Reroll", out string value) && bool.TryParse(value, out result)) & result;
            int targetEntityId = (int.TryParse(action.SourceEntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2) ? result2 : 0);
            int[] targetEntityIds = (
                from entityId in ReadEntityIdListParameter(action)select int.TryParse(entityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3) ? result3 : 0 into entityId
                    where entityId > 0
                    select entityId).ToArray();
            BridgeResponse bridgeResponse = ((!useJail319RerollFlow) ? (await _client.SendAsync("selectFriendlyChoice", new { optionIndex = action.OptionIndex.Value, targetEntityId = targetEntityId, targetEntityIds = targetEntityIds }, 10000, cancellationToken)) : (await _client.SendAsync("selectFriendlyChoice", new { mode = "Jail319Reroll", optionIndex = action.OptionIndex.Value }, 10000, cancellationToken)));
            BridgeResponse bridgeResponse2 = bridgeResponse;
            if (bridgeResponse2.Data.ValueKind == JsonValueKind.Object && bridgeResponse2.Data.TryGetProperty("selectionDeferred", out var value2) && value2.ValueKind == JsonValueKind.True)
            {
                _darkGiftSelectionDeferred = true;
                LogMessage("黑暗发现候选尚未全部展示或已变化，等待最新局面后重试推荐选择");
                return false;
            }

            if (IsAccepted(bridgeResponse2))
            {
                if (useJail319RerollFlow)
                {
                    if (await WaitForJail319RerollProgressAsync(action, cancellationToken))
                    {
                        LogMessage("万能钥匙第 4 项已通过 RerollUIManager 刷新，新的发现候选已就绪");
                        return true;
                    }

                    LogMessage("万能钥匙刷新响应已提交，但发现候选尚未更新，下一轮重试同一刷新指令");
                    return false;
                }

                bool num = bridgeResponse2.Data.TryGetProperty("isSubOption", out var value3) && value3.ValueKind == JsonValueKind.True;
                bool isEntityTargetChoice = bridgeResponse2.Data.TryGetProperty("isEntityTargetChoice", out var value4) && value4.ValueKind == JsonValueKind.True;
                if (num)
                {
                    await Task.Delay(IsAi2FastTimingAction(action) ? 60 : 180, cancellationToken);
                    BridgeResponse bridgeResponse3 = await _client.SendAsync("confirmFriendlyChoice", new { }, 10000, cancellationToken);
                    if (!bridgeResponse3.Ok)
                    {
                        LogMessage($"第 {action.OptionIndex.Value + 1} 项已选中，但内部确认未完成：{bridgeResponse3.Error}");
                        return false;
                    }
                }

                ConstructedActionExecutor constructedActionExecutor = this;
                string message;
                if (isEntityTargetChoice)
                {
                    message = ((targetEntityIds.Length > 1) ? $"已通过 GameState 精确提交 {targetEntityIds.Length} 个目标实体：{string.Join("、", targetEntityIds)}" : $"已通过 GameState 精确提交目标实体 {targetEntityId}");
                }
                else
                {
                    message = $"已通过 ChoiceCardMgr 内部响应选择第 {action.OptionIndex.Value + 1} 项";
                }

                constructedActionExecutor.LogMessage(message);
                _choiceVisualConfirmationPending = true;
                _choiceVisualConfirmationFastTiming = IsAi2FastTimingAction(action);
                if (await ConfirmFriendlyChoiceDismissedAsync(allowCleanup: true, _choiceVisualConfirmationFastTiming, cancellationToken))
                {
                    _choiceVisualConfirmationPending = false;
                    _choiceVisualConfirmationFastTiming = false;
                    LogMessage("已确认发现选择阶段结束且候选卡全部退出界面");
                    return true;
                }

                ConstructedPhase phase = (await _reader.ReadAsync(cancellationToken)).State.Phase;
                if ((uint)(phase - 5) <= 1u)
                {
                    _choiceVisualConfirmationPending = false;
                    _choiceVisualConfirmationFastTiming = false;
                    LogMessage("发现选择仍处于活动阶段，下一轮重试盒子指定的同一选项");
                }
                else
                {
                    LogMessage("发现选择已生效，但残留候选卡尚未退出，保持阻塞等待清理");
                }

                return false;
            }

            LogMessage(bridgeResponse2.Ok ? $"发现第 {action.OptionIndex.Value + 1} 项尚未被客户端接受，下一帧重试" : ("发现选择内部响应失败：" + bridgeResponse2.Error));
        }

        return false;
    }

    private async Task<bool> WaitForJail319RerollProgressAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        action.Parameters.TryGetValue("hsauto.jail319ChoiceSignature", out string previousSignature);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(150, cancellationToken);
            UnityBridgeConstructedObservedState unityBridgeConstructedObservedState = await _reader.ReadAsync(cancellationToken);
            ConstructedPhase phase = unityBridgeConstructedObservedState.State.Phase;
            if ((uint)(phase - 5) > 1u)
            {
                return true;
            }

            IReadOnlyList<ConstructedEntity> readOnlyList = ((unityBridgeConstructedObservedState.State.ChoiceOptions.Count > 0) ? unityBridgeConstructedObservedState.State.ChoiceOptions : unityBridgeConstructedObservedState.State.DiscoverOptions);
            if (readOnlyList.Count != 0 && !string.Equals(string.Join("|", readOnlyList.Select((ConstructedEntity option) => option.EntityId + ":" + option.CardId)), previousSignature, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> ConfirmFriendlyChoiceDismissedAsync(bool allowCleanup, bool fastTiming, CancellationToken cancellationToken)
    {
        int pollDelayMs = (fastTiming ? 100 : 250);
        int pollAttempts = (fastTiming ? 50 : 20);
        int cleanupAttempts = (fastTiming ? 20 : 8);
        DateTimeOffset? phaseExitedAt = null;
        ConstructedPhase phase;
        for (int attempt = 0; attempt < pollAttempts; attempt++)
        {
            await Task.Delay(pollDelayMs, cancellationToken);
            BridgeResponse bridgeResponse = await _client.SendAsync("choiceCards", new { }, 3000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                continue;
            }

            if (((bridgeResponse.Data.TryGetProperty("visibleCount", out var value) && value.TryGetInt32(out var value2)) ? value2 : ((bridgeResponse.Data.TryGetProperty("count", out var value3) && value3.TryGetInt32(out var value4) && value4 != 0) ? 1 : 0)) > 0)
            {
                phase = (await _reader.ReadAsync(cancellationToken)).State.Phase;
                if ((uint)(phase - 5) <= 1u)
                {
                    phaseExitedAt = null;
                    continue;
                }

                phaseExitedAt.GetValueOrDefault();
                if (!phaseExitedAt.HasValue)
                {
                    DateTimeOffset utcNow = DateTimeOffset.UtcNow;
                    phaseExitedAt = utcNow;
                }

                TimeSpan timeSpan = (fastTiming ? TimeSpan.FromMilliseconds(300.0) : TimeSpan.FromSeconds(1.0));
                if (DateTimeOffset.UtcNow - phaseExitedAt.Value >= timeSpan)
                {
                    break;
                }
            }
            else
            {
                phase = (await _reader.ReadAsync(cancellationToken)).State.Phase;
                if ((uint)(phase - 5) > 1u)
                {
                    return true;
                }
            }
        }

        phase = (await _reader.ReadAsync(cancellationToken)).State.Phase;
        bool flag = !allowCleanup;
        if (!flag)
        {
            bool flag2 = (uint)(phase - 5) <= 1u;
            flag = flag2;
        }

        if (flag)
        {
            return false;
        }

        BridgeResponse bridgeResponse2 = await _client.SendAsync("cleanupFriendlyChoiceVisuals", new { }, 5000, cancellationToken);
        if (!bridgeResponse2.Ok)
        {
            LogMessage("清理发现残留候选卡失败：" + bridgeResponse2.Error);
            return false;
        }

        for (int attempt = 0; attempt < cleanupAttempts; attempt++)
        {
            await Task.Delay(pollDelayMs, cancellationToken);
            BridgeResponse bridgeResponse3 = await _client.SendAsync("choiceCards", new { }, 3000, cancellationToken);
            if (bridgeResponse3.Ok && (!bridgeResponse3.Data.TryGetProperty("visibleCount", out var value5) || !value5.TryGetInt32(out var value6) || value6 == 0))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryConfirmMulliganAsync(IReadOnlyList<int>? keptEntityIds, CancellationToken cancellationToken)
    {
        object arguments = ((keptEntityIds == null) ? ((object)new
        {
        }

        ) : ((object)new
        {
            keptEntityIds
        }

        ));
        BridgeResponse bridgeResponse = await _client.SendAsync("confirmMulliganHero", arguments, 10000, cancellationToken);
        bool flag = !bridgeResponse.Data.TryGetProperty("choicesSubmitted", out var value) || value.ValueKind == JsonValueKind.True;
        if (bridgeResponse.Ok & flag)
        {
            LogMessage("已通过 MulliganManager 内部确认起手换牌");
            return true;
        }

        if (bridgeResponse.Ok)
        {
            string text = ((bridgeResponse.Data.TryGetProperty("choiceError", out var value2) && value2.ValueKind == JsonValueKind.String) ? value2.GetString() : "客户端未确认 Network.SendChoices 已提交");
            LogMessage("内部确认起手换牌未提交：" + text + "，改用原生确认按钮");
        }
        else
        {
            LogMessage("内部确认起手换牌不可用：" + bridgeResponse.Error);
        }

        object[] array = new object[7]
        {
            new
            {
                path = "/MulliganButton(Clone)"
            },
            new
            {
                component = "MulliganButton"
            },
            new
            {
                component = "NormalButton",
                nameContains = "MulliganButton"
            },
            new
            {
                nameContains = "Mulligan"
            },
            new
            {
                textContains = "确认"
            },
            new
            {
                textContains = "保留"
            },
            new
            {
                textContains = "Confirm"
            }
        };
        foreach (object selector in array)
        {
            if (await ClickMulliganConfirmSelectorAsync(selector, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<int> MulliganKeptEntityIds(ConstructedGameState state, IReadOnlyList<ConstructedAction> actions)
    {
        HashSet<string> replacements = (
            from action in actions
            where action.Type == ConstructedActionType.MulliganReplace
            select action.SourceEntityId into id
                where !string.IsNullOrWhiteSpace(id)select id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (
            from card in state.Hand
            where !ConstructedMulliganCardIdentity.IsCoin(card)
            where !replacements.Contains(card.EntityId)select int.TryParse(card.EntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0 into entityId
                where entityId > 0
                select entityId).Distinct().ToArray();
    }

    private async Task<bool> TrySelectMulliganReplacementAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        object obj = BuildNetworkResponseSelector(action, preferCardSelector: true);
        if (obj != null)
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("selectMulliganCard", new { selector = obj, replace = true }, 10000, cancellationToken);
            if (bridgeResponse.Ok)
            {
                LogMessage("已通过 MulliganManager 内部选中起手替换牌 " + action.SourceEntityId);
                return true;
            }

            if (!bridgeResponse.Ok)
            {
                LogMessage("内部选择起手替换牌不可用：" + bridgeResponse.Error);
            }
        }

        return await ClickSemanticObjectAsync(action, "选择起手替换牌", CardClickComponents, cancellationToken);
    }

    private async Task<bool> ClickMulliganConfirmSelectorAsync(object selector, CancellationToken cancellationToken)
    {
        string[] array = new string[4]
        {
            "NormalButton",
            "MulliganButton",
            "PegUIElement",
            "Clickable"
        };
        foreach (string component in array)
        {
            if (await TryPressReleaseAsync(selector, component, cancellationToken))
            {
                await Task.Delay(120, cancellationToken);
                await TryTapAsync(selector, component, cancellationToken);
                LogMessage("确认起手换牌");
                return true;
            }
        }

        if (await ClickBySelectorAsync(selector, "确认起手换牌", ControlClickComponents, cancellationToken))
        {
            return true;
        }

        return false;
    }

    private async Task<bool> TryTapAsync(object selector, string componentContains, CancellationToken cancellationToken)
    {
        return (await _client.SendAsync("invokeMethod", new { selector = selector, componentContains = componentContains, method = "TriggerTap" }, 10000, cancellationToken)).Ok;
    }

    private async Task<bool> TryEndTurnAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("constructedEndTurn", new { }, 10000, cancellationToken);
        if (IsAccepted(bridgeResponse))
        {
            LogMessage("结束传统对战回合已通过 GameState.SendOption 执行");
            return true;
        }

        if (!bridgeResponse.Ok)
        {
            LogMessage("内部结束回合不可用：" + bridgeResponse.Error);
        }

        if (!AllowLegacyEndTurnObjectFallback)
        {
            LogMessage("传统基础结束回合暂不可用，等待下一份稳定局面，不执行界面对象扫描");
            return false;
        }

        object[] array = new object[4]
        {
            new
            {
                nameContains = "EndTurnButton"
            },
            new
            {
                component = "EndTurnButton"
            },
            new
            {
                textContains = "结束回合"
            },
            new
            {
                textContains = "End Turn"
            }
        };
        foreach (object selector in array)
        {
            if (await ClickBySelectorAsync(selector, "已发送传统对战结束回合点击", ControlClickComponents, cancellationToken))
            {
                if (await ConfirmEndTurnTransitionAsync(cancellationToken))
                {
                    LogMessage("已通过实时局面确认传统对战回合结束");
                    return true;
                }

                LogMessage("结束回合点击虽被界面对象接收，但实时局面未进入对手回合，不计为成功");
            }
        }

        return false;
    }

    private async Task<bool> ConfirmEndTurnTransitionAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await Task.Delay(350, cancellationToken);
            UnityBridgeConstructedObservedState unityBridgeConstructedObservedState = await _reader.ReadAsync(cancellationToken);
            ConstructedPhase phase = unityBridgeConstructedObservedState.State.Phase;
            if ((phase == ConstructedPhase.OpponentTurn || phase == ConstructedPhase.GameOver) ? true : false)
            {
                return true;
            }

            phase = unityBridgeConstructedObservedState.State.Phase;
            if ((uint)(phase - 5) <= 1u)
            {
                return false;
            }
        }

        return false;
    }

    private async Task<bool> TryConcedeAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("constructedConcede", new { }, 10000, cancellationToken);
        if (IsAccepted(bridgeResponse))
        {
            LogMessage("传统对战认输已通过 GameState.Concede 执行");
            return true;
        }

        LogMessage(bridgeResponse.Ok ? "传统对战认输未被客户端接受" : ("传统对战认输内部方法不可用：" + bridgeResponse.Error));
        return false;
    }

    private async Task<bool> TryNetworkResponseAsync(ConstructedAction action, bool preferCardSelector, CancellationToken cancellationToken)
    {
        object obj = BuildNetworkResponseSelector(action, preferCardSelector);
        if (obj == null)
        {
            return false;
        }

        BridgeResponse bridgeResponse = await SendNetworkResponseAsync(obj, checkValidInput: true, cancellationToken);
        if (IsAccepted(bridgeResponse))
        {
            LogMessage(ActionLabel(action) + " 已通过 InputManager.DoNetworkResponse 执行");
            return true;
        }

        if (!bridgeResponse.Ok)
        {
            LogMessage(ActionLabel(action) + " 内部响应不可用：" + bridgeResponse.Error);
        }

        return false;
    }

    private async Task<BridgeResponse> SendNetworkResponseAsync(object selector, bool checkValidInput, CancellationToken cancellationToken)
    {
        return await _client.SendAsync("networkResponse", new { selector = selector, checkValidInput = checkValidInput, wantDeckOption = false }, 10000, cancellationToken);
    }

    private async Task<bool> ClickSemanticObjectAsync(ConstructedAction action, string label, IReadOnlyList<string> componentHints, CancellationToken cancellationToken)
    {
        object obj = BuildNetworkResponseSelector(action, preferCardSelector: true);
        bool flag = obj != null;
        if (flag)
        {
            flag = await ClickBySelectorAsync(obj, label, componentHints, cancellationToken);
        }

        return flag;
    }

    private async Task ClickTargetSelectorAsync(ConstructedAction action, CancellationToken cancellationToken)
    {
        object obj = BuildTargetSelector(action);
        if (obj != null)
        {
            await ClickBySelectorAsync(obj, "点击传统动作目标", CardClickComponents, cancellationToken);
        }
    }

    private async Task<bool> ClickBySelectorAsync(object selector, string label, IReadOnlyList<string> componentHints, CancellationToken cancellationToken)
    {
        foreach (string componentHint in componentHints)
        {
            if (await TryPressReleaseAsync(selector, componentHint, cancellationToken))
            {
                LogMessage(label);
                return true;
            }
        }

        BridgeResponse bridgeResponse = await _client.SendAsync("clickObject", new { selector }, 10000, cancellationToken);
        if (bridgeResponse.Ok)
        {
            LogMessage(label);
        }
        else
        {
            LogMessage(label + " 失败：" + bridgeResponse.Error);
        }

        return bridgeResponse.Ok;
    }

    private async Task<bool> TryPressReleaseAsync(object selector, string componentContains, CancellationToken cancellationToken)
    {
        if (!(await _client.SendAsync("invokeMethod", new { selector = selector, componentContains = componentContains, method = "TriggerPress" }, 10000, cancellationToken)).Ok)
        {
            return false;
        }

        await Task.Delay(120, cancellationToken);
        return (await _client.SendAsync("invokeMethod", new { selector = selector, componentContains = componentContains, method = "TriggerRelease" }, 10000, cancellationToken)).Ok;
    }

    private static object? BuildNetworkResponseSelector(ConstructedAction action, bool preferCardSelector)
    {
        if (preferCardSelector && action.Parameters.TryGetValue("unity.cardInstanceId", out string value) && int.TryParse(value, out var result))
        {
            return new
            {
                instanceId = result
            };
        }

        if (preferCardSelector && action.Parameters.TryGetValue("unity.cardPath", out string value2) && !string.IsNullOrWhiteSpace(value2))
        {
            return new
            {
                path = value2
            };
        }

        if (action.Parameters.TryGetValue("unity.instanceId", out string value3) && int.TryParse(value3, out var result2))
        {
            return new
            {
                instanceId = result2
            };
        }

        if (action.Parameters.TryGetValue("unity.path", out string value4) && !string.IsNullOrWhiteSpace(value4))
        {
            return new
            {
                path = value4
            };
        }

        if (action.Parameters.TryGetValue("unity.name", out string value5) && !string.IsNullOrWhiteSpace(value5))
        {
            return new
            {
                name = value5
            };
        }

        return null;
    }

    private static object? BuildTargetSelector(ConstructedAction action)
    {
        if (action.Parameters.TryGetValue("unity.targetCardInstanceId", out string value) && int.TryParse(value, out var result))
        {
            return new
            {
                instanceId = result
            };
        }

        if (action.Parameters.TryGetValue("unity.targetInstanceId", out string value2) && int.TryParse(value2, out var result2))
        {
            return new
            {
                instanceId = result2
            };
        }

        if (action.Parameters.TryGetValue("unity.targetCardPath", out string value3) && !string.IsNullOrWhiteSpace(value3))
        {
            return new
            {
                path = value3
            };
        }

        if (action.Parameters.TryGetValue("unity.targetPath", out string value4) && !string.IsNullOrWhiteSpace(value4))
        {
            return new
            {
                path = value4
            };
        }

        return null;
    }

    private static bool IsAccepted(BridgeResponse response)
    {
        if (response.Ok && response.Data.TryGetProperty("accepted", out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static bool IsUnknownCommand(BridgeResponse response)
    {
        if (!response.Ok)
        {
            return response.Error?.Contains("Unknown command", StringComparison.OrdinalIgnoreCase) ?? false;
        }

        return false;
    }

    private static bool IsActionAllowedInPhase(ConstructedPhase phase, ConstructedAction action)
    {
        switch (action.Type)
        {
            case ConstructedActionType.MulliganKeep:
            case ConstructedActionType.MulliganReplace:
            case ConstructedActionType.ConfirmMulligan:
                return phase == ConstructedPhase.Mulligan;
            case ConstructedActionType.ChooseOption:
                return (uint)(phase - 5) <= 1u;
            case ConstructedActionType.PlayCard:
            case ConstructedActionType.PlayCardWithTarget:
            case ConstructedActionType.Attack:
            case ConstructedActionType.UseHeroPower:
            case ConstructedActionType.UseHeroPowerWithTarget:
            case ConstructedActionType.UseLocation:
            case ConstructedActionType.UseLocationWithTarget:
            case ConstructedActionType.TradeCard:
            case ConstructedActionType.InternalOption:
            case ConstructedActionType.EndTurn:
            case ConstructedActionType.CancelPendingInput:
                return phase == ConstructedPhase.LocalTurn;
            case ConstructedActionType.Concede:
                return phase switch
                {
                    ConstructedPhase.Mulligan => IsAutomaticConcede(action),
                    ConstructedPhase.LocalTurn => true,
                    _ => false,
                };
            case ConstructedActionType.NoOp:
            case ConstructedActionType.Wait:
                return true;
            default:
                return false;
        }
    }

    private static bool IsAutomaticConcede(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.Parameters.TryGetValue("hsauto.automaticConcede", out value) && bool.TryParse(value, out result)) & result;
    }

    private static bool IsStillLegal(ConstructedGameState state, ConstructedAction action, ConstructedEntity? source, ConstructedEntity? target, out string reason)
    {
        reason = "";
        if ((object)source != null && action.ExpectedCost.HasValue && action.Type != ConstructedActionType.TradeCard && source.Cost != action.ExpectedCost.Value)
        {
            reason = $"实际费用 {source.Cost} 与计划费用 {action.ExpectedCost.Value} 不一致";
            return false;
        }

        switch (action.Type)
        {
            case ConstructedActionType.PlayCard:
            case ConstructedActionType.PlayCardWithTarget:
            {
                if ((object)source == null || source.Zone != ConstructedZone.Hand)
                {
                    reason = "出牌源不在手牌";
                    return false;
                }

                bool flag = HasMatchingPlayableAction(state, action);
                bool flag2 = flag && HasInternalOption(action);
                bool result = default;
                bool flag3 = (flag && action.Parameters.TryGetValue("plan.allowStaleManaAfterCoin", out string value) && bool.TryParse(value, out result)) & result;
                if (source.Cost > state.ManaAvailable && !flag2 && !flag3)
                {
                    reason = $"费用不足：需要 {source.Cost}，当前 {state.ManaAvailable}";
                    return false;
                }

                if (source.Tags.TryGetValue("unity.hasResponse", out string value2) && bool.TryParse(value2, out var result2) && !result2)
                {
                    reason = "手牌当前不可响应";
                    return false;
                }

                if (IsNeteaseSourceSelection(action) && state.CurrentPlayableActions.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.PlayCardWithTarget && string.Equals(candidate.SourceEntityId, source.EntityId, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                if (flag || state.CurrentPlayableActions.Count == 0)
                {
                    return true;
                }

                reason = BuildNoMatchingPlayableActionReason(state, action);
                return false;
            }

            case ConstructedActionType.Attack:
                if ((object)source == null)
                {
                    reason = "攻击源已不存在";
                    return false;
                }

                if ((object)target == null)
                {
                    reason = "攻击目标不存在";
                    return false;
                }

                if (HasMatchingPlayableAction(state, action))
                {
                    return true;
                }

                if (ConstructedActionRules.TargetableEnemyTaunts(state).ToArray().Length != 0 && (target.Zone != ConstructedZone.EnemyBoard || !target.HasTaunt))
                {
                    reason = "敌方有嘲讽，必须先攻击可被攻击的嘲讽随从";
                    return false;
                }

                if (!source.CanAttack)
                {
                    reason = "攻击源当前不可攻击";
                    return false;
                }

                if (state.CurrentPlayableActions.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.Attack && IsInternalPlayableAction(candidate)))
                {
                    reason = "客户端内部 OptionsPacket 不允许该攻击源或目标";
                    return false;
                }

                return state.CurrentPlayableActions.Count == 0;
            case ConstructedActionType.UseHeroPower:
            case ConstructedActionType.UseHeroPowerWithTarget:
                if (HasMatchingPlayableAction(state, action) || state.CurrentPlayableActions.Count == 0)
                {
                    return true;
                }

                reason = BuildNoMatchingPlayableActionReason(state, action);
                return false;
            case ConstructedActionType.UseLocation:
            case ConstructedActionType.UseLocationWithTarget:
            {
                ConstructedEntity? constructedEntity = source;
                if ((object)constructedEntity == null || constructedEntity.Zone != ConstructedZone.FriendlyBoard || !source.IsLocation)
                {
                    reason = "地标源已不存在或不是我方地标";
                    return false;
                }

                if (source.LocationCooldown > 0)
                {
                    reason = "地标仍在冷却，忽略客户端残留选项";
                    return false;
                }

                if (!HasMatchingPlayableAction(state, action))
                {
                    reason = "客户端内部 OptionsPacket 不再允许该地标动作";
                    return false;
                }

                return true;
            }

            case ConstructedActionType.TradeCard:
            {
                ConstructedEntity? constructedEntity2 = source;
                if ((object)constructedEntity2 == null || constructedEntity2.Zone != ConstructedZone.Hand || !source.IsTradeable)
                {
                    reason = "交易源不在手牌或不具有可交易机制";
                    return false;
                }

                if (state.ManaAvailable < 1)
                {
                    reason = "交易需要至少 1 点可用法力";
                    return false;
                }

                if (!HasMatchingPlayableAction(state, action))
                {
                    reason = "客户端内部 OptionsPacket 不再允许交易该牌";
                    return false;
                }

                return true;
            }

            case ConstructedActionType.InternalOption:
                if (!HasMatchingPlayableAction(state, action) || !IsInternalPlayableAction(action))
                {
                    reason = "客户端内部 OptionsPacket 不再允许该特殊动作";
                    return false;
                }

                return true;
            default:
                return true;
        }
    }

    private static string BuildNoMatchingPlayableActionReason(ConstructedGameState state, ConstructedAction action)
    {
        ConstructedAction[] array = state.CurrentPlayableActions.Where((ConstructedAction candidate) => string.Equals(candidate.SourceEntityId, action.SourceEntityId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (array.Length == 0)
        {
            return ActionTypeLabel(action.Type) + "的源对象当前不在客户端可行动作列表中";
        }

        if (action.Type == ConstructedActionType.PlayCard && array.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.PlayCardWithTarget))
        {
            return "该手牌当前需要指定目标，不能按无目标出牌执行";
        }

        if (action.Type == ConstructedActionType.PlayCardWithTarget && array.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.PlayCard))
        {
            return "该手牌当前是无目标出牌，不应指定目标";
        }

        if (action.Type == ConstructedActionType.UseHeroPower && array.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.UseHeroPowerWithTarget))
        {
            return "该英雄技能当前需要指定目标，不能按无目标技能执行";
        }

        if (action.Type == ConstructedActionType.UseHeroPowerWithTarget && array.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.UseHeroPower))
        {
            return "该英雄技能当前是无目标技能，不应指定目标";
        }

        return ActionTypeLabel(action.Type) + "的源对象和目标组合不在客户端可行动作列表中";
    }

    private static bool HasMatchingPlayableAction(ConstructedGameState state, ConstructedAction action)
    {
        return state.CurrentPlayableActions.Any((ConstructedAction candidate) => candidate.Type == action.Type && (string.IsNullOrWhiteSpace(action.SourceEntityId) || string.Equals(candidate.SourceEntityId, action.SourceEntityId, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(action.TargetEntityId) || string.Equals(candidate.TargetEntityId, action.TargetEntityId, StringComparison.OrdinalIgnoreCase)) && (action.Type != ConstructedActionType.InternalOption || !action.OptionIndex.HasValue || candidate.OptionIndex == action.OptionIndex));
    }

    private static bool IsInternalPlayableAction(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.Parameters.TryGetValue("unity.internalOption", out value) && bool.TryParse(value, out result)) & result;
    }

    private static bool RequiresSource(ConstructedActionType actionType)
    {
        if ((uint)(actionType - 2) <= 1u || (uint)(actionType - 5) <= 9u)
        {
            return true;
        }

        return false;
    }

    private static bool RequiresTarget(ConstructedActionType actionType)
    {
        switch (actionType)
        {
            case ConstructedActionType.PlayCardWithTarget:
            case ConstructedActionType.Attack:
            case ConstructedActionType.UseHeroPowerWithTarget:
            case ConstructedActionType.UseLocationWithTarget:
                return true;
            default:
                return false;
        }
    }

    private static ConstructedEntity? FindAnyEntityById(ConstructedGameState state, string? entityId)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return null;
        }

        return FindById(state.Hand, entityId) ?? FindById(state.FriendlyBoard, entityId) ?? FindById(state.EnemyBoard, entityId) ?? FindById(state.DiscoverOptions, entityId) ?? FindById(state.ChoiceOptions, entityId) ?? (string.Equals(state.LocalHero?.EntityId, entityId, StringComparison.OrdinalIgnoreCase) ? state.LocalHero : null) ?? (string.Equals(state.OpponentHero?.EntityId, entityId, StringComparison.OrdinalIgnoreCase) ? state.OpponentHero : null) ?? (string.Equals(state.FriendlyWeapon?.EntityId, entityId, StringComparison.OrdinalIgnoreCase) ? state.FriendlyWeapon : null) ?? (string.Equals(state.EnemyWeapon?.EntityId, entityId, StringComparison.OrdinalIgnoreCase) ? state.EnemyWeapon : null) ?? FindById(state.GetLocalHeroPowers(), entityId);
    }

    private static ConstructedEntity? FindById(IEnumerable<ConstructedEntity> entities, string entityId)
    {
        return entities.FirstOrDefault((ConstructedEntity entity) => string.Equals(entity.EntityId, entityId, StringComparison.OrdinalIgnoreCase));
    }

    private static void CopyEntityTags(IDictionary<string, string> parameters, ConstructedEntity entity)
    {
        foreach (KeyValuePair<string, string> tag in entity.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag.Value))
            {
                parameters[tag.Key] = tag.Value;
            }
        }
    }

    private static void ApplyTargetParameters(IDictionary<string, string> parameters, ConstructedEntity target)
    {
        CopyTag(target, parameters, "unity.path", "unity.targetPath");
        CopyTag(target, parameters, "unity.instanceId", "unity.targetInstanceId");
        CopyTag(target, parameters, "unity.cardPath", "unity.targetCardPath");
        CopyTag(target, parameters, "unity.cardInstanceId", "unity.targetCardInstanceId");
        CopyTag(target, parameters, "unity.name", "unity.targetName");
    }

    private static void CopyTag(ConstructedEntity source, IDictionary<string, string> parameters, string sourceKey, string targetKey)
    {
        if (source.Tags.TryGetValue(sourceKey, out string value) && !string.IsNullOrWhiteSpace(value))
        {
            parameters[targetKey] = value;
        }
    }

    private static bool IsExecutableAction(ConstructedAction action)
    {
        ConstructedActionType type = action.Type;
        if (type != ConstructedActionType.NoOp && type != ConstructedActionType.Wait)
        {
            return type != ConstructedActionType.MulliganKeep;
        }

        return false;
    }

    private static bool IsNeteaseSourceSelection(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.Parameters.TryGetValue("netease.selectSourceOnly", out value) && bool.TryParse(value, out result)) & result;
    }

    private static bool IsNeteaseAction(ConstructedAction action)
    {
        return action.Parameters.ContainsKey("netease.optionId");
    }

    private static bool IsTagTrue(ConstructedGameState state, string key)
    {
        string value;
        bool result = default;
        return (state.Tags.TryGetValue(key, out value) && bool.TryParse(value, out result)) & result;
    }

    private static int ReadTagInt(ConstructedGameState state, string key)
    {
        if (!state.Tags.TryGetValue(key, out string value) || !int.TryParse(value, out var result))
        {
            return 0;
        }

        return result;
    }

    private static string ActionLabel(ConstructedAction action)
    {
        string text = ActionTypeLabel(action.Type);
        if (action.Type == ConstructedActionType.InternalOption && IsAi2FastTimingAction(action) && action.Parameters.TryGetValue("netease.actionName", out string value) && !string.IsNullOrWhiteSpace(value))
        {
            string value3;
            if (action.Parameters.TryGetValue("unity.cardId", out string value2))
            {
                value3 = value2;
            }
            else
            {
                value3 = (action.Parameters.TryGetValue("cardId", out string value4) ? value4 : "");
            }

            text += (string.IsNullOrWhiteSpace(value3) ? ("[" + value + "]") : $"[{value}/{value3}]");
        }

        string text2 = (string.IsNullOrWhiteSpace(action.SourceEntityId) ? "" : (" " + action.SourceEntityId));
        IReadOnlyList<string> readOnlyList = ReadEntityIdListParameter(action);
        string text3;
        if (readOnlyList.Count > 1)
        {
            text3 = "，目标 " + string.Join("、", readOnlyList);
        }
        else
        {
            text3 = (string.IsNullOrWhiteSpace(action.TargetEntityId) ? "" : ("，目标 " + action.TargetEntityId));
        }

        return text + text2 + text3;
    }

    private static string ActionTypeLabel(ConstructedActionType type)
    {
        return type switch
        {
            ConstructedActionType.NoOp => "无操作",
            ConstructedActionType.Wait => "等待",
            ConstructedActionType.MulliganKeep => "起手保留",
            ConstructedActionType.MulliganReplace => "起手换掉",
            ConstructedActionType.ConfirmMulligan => "确认起手",
            ConstructedActionType.PlayCard => "打出手牌",
            ConstructedActionType.PlayCardWithTarget => "指定目标出牌",
            ConstructedActionType.Attack => "攻击",
            ConstructedActionType.UseHeroPower => "使用英雄技能",
            ConstructedActionType.UseHeroPowerWithTarget => "指定目标英雄技能",
            ConstructedActionType.UseLocation => "激活地标",
            ConstructedActionType.UseLocationWithTarget => "指定目标激活地标",
            ConstructedActionType.TradeCard => "交易卡牌",
            ConstructedActionType.InternalOption => "执行客户端合法特殊操作",
            ConstructedActionType.ChooseOption => "选择选项",
            ConstructedActionType.EndTurn => "结束回合",
            ConstructedActionType.CancelPendingInput => "取消待定输入",
            ConstructedActionType.Concede => "认输",
            _ => type.ToString(),
        };
    }

    private static string PhaseLabel(ConstructedPhase phase)
    {
        return phase switch
        {
            ConstructedPhase.Queue => "匹配中",
            ConstructedPhase.Mulligan => "起手换牌",
            ConstructedPhase.LocalTurn => "我方回合",
            ConstructedPhase.OpponentTurn => "对手回合",
            ConstructedPhase.Discover => "发现选择",
            ConstructedPhase.Choice => "选择阶段",
            ConstructedPhase.GameOver => "对局结束",
            ConstructedPhase.Unknown => "未知阶段",
            _ => phase.ToString(),
        };
    }

    private void LogMessage(string message)
    {
        Log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [传统执行] {message}");
    }
}
