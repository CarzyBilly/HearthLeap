// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Automation;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;

namespace HsAuto.Core.Strategy;
public sealed class NeteaseBoxConstructedStrategy : IConstructedStrategy, IInitializableConstructedStrategy, IConstructedActionExecutionObserver, IConstructedWinRateProvider
{
    private readonly record struct LocalTurnProgressSnapshot(int OptionsPacketId, int ManaAvailable, int ManaTotal, int HeroHealth, int HeroArmor, int OpponentHealth, int OpponentArmor, int LocalHeroSignature, int OpponentHeroSignature, int HandSignature, int FriendlyBoardSignature, int EnemyBoardSignature, int FriendlyWeaponSignature, int EnemyWeaponSignature, int LocalHeroPowersSignature, int PlayableActionsSignature, int SecretsCount);
    public const string StrategyName = "NeteaseBoxConstructed";
    public const string AccountRecoveryStatusParameter = "hsauto.constructedBoxAccountRecoveryStatus";
    public const string StopAfterRecoveryConcedeParameter = "hsauto.stopAfterConstructedBoxRecoveryConcede";
    public const string StallKindParameter = "hsauto.neteaseConstructedStallKind";
    public const string RecommendationStallKind = "Recommendation";
    public const string AuthenticationStallKind = "Authentication";
    private readonly INeteaseBoxRecommendationSource _source;
    private readonly NeteaseBoxConstructedRecoveryState _recoveryState;
    private readonly TimeProvider _timeProvider;
    private readonly ConstructedMissingOptionsPacketRecovery _missingOptionsRecovery;
    private readonly bool _requireManaSync;
    private readonly bool _enableMissingOptionsRecovery;
    private readonly bool _enableDirectCompatibility;
    private bool _initialized;
    private int _hearthstoneProcessId;
    private int _lastOptionsPacketId;
    private ConstructedPhase _lastPhase;
    private DateTimeOffset _gameEpoch = DateTimeOffset.MinValue;
    private string _lastStatus = "";
    private DateTimeOffset _lastStatusAt = DateTimeOffset.MinValue;
    private string? _lastExecutedRecommendationKey;
    private string? _lastExecutedJail319ObservationKey;
    private string _trackedLocalMatchId = "";
    private int _trackedLocalTurn;
    private DateTimeOffset? _missingRecommendationSince;
    private DateTimeOffset? _noSuccessfulActionSince;
    private DateTimeOffset? _boxRecoveryCooldownStartedAt;
    private LocalTurnProgressSnapshot? _lastLocalTurnProgress;
    private bool _recoveryMonitoringEnabled;
    private bool _recoveryMonitoringConfigurationInitialized;
    private ConstructedWinRateSnapshot? _latestWinRate;
    private string _winRateMatchId = "";
    private DateTimeOffset _winRateGameEpoch = DateTimeOffset.MinValue;
    private bool _winRateCaptureStarted;
    private Task? _opponentTurnWinRateRefreshTask;
    private bool _hasObservedState;
    private bool _startupLocalActionSucceeded;
    private bool _mulliganSubmitted;
    private int _startupRelocations;
    private DateTimeOffset? _startupMissingSince;
    private NeteaseBoxRecommendationObservation? _mulliganLease;
    private string _mulliganLeaseScope = "";
    private DateTimeOffset _mulliganLeaseUntil;
    private bool _matchBoundaryObserved;
    private string _strategyMatchId = "";
    public string Name => "NeteaseBoxConstructed";

    public event Action<string>? Log;
    public NeteaseBoxConstructedStrategy(INeteaseBoxRecommendationSource? source = null, bool enableDiagnostics = false, NeteaseBoxConstructedRecoveryState? recoveryState = null, TimeProvider? timeProvider = null, bool requireManaSync = true, bool enableMissingOptionsRecovery = true)
    {
        _source = source ?? new NeteaseBoxRecommendationSource(enableDiagnostics);
        _recoveryState = recoveryState ?? new NeteaseBoxConstructedRecoveryState();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _missingOptionsRecovery = new ConstructedMissingOptionsPacketRecovery(_timeProvider, "盒子AI");
        _requireManaSync = requireManaSync;
        _enableMissingOptionsRecovery = enableMissingOptionsRecovery;
        _enableDirectCompatibility = false;
        _source.Log += (string message) =>
        {
            Log?.Invoke(message);
        };
    }

    public bool TryGetLatestWinRate(string matchId, out ConstructedWinRateSnapshot snapshot)
    {
        snapshot = _latestWinRate ?? new ConstructedWinRateSnapshot("", double.NaN, DateTimeOffset.MinValue, 0);
        if ((object)_latestWinRate != null)
        {
            return string.Equals(_latestWinRate.MatchId, NormalizeWinRateMatchId(matchId), StringComparison.Ordinal);
        }

        return false;
    }

    public void ResetWinRate()
    {
        _latestWinRate = null;
        _winRateMatchId = "";
        _winRateGameEpoch = DateTimeOffset.MinValue;
        _winRateCaptureStarted = false;
        if (_source is INeteaseBoxWinRateSource neteaseBoxWinRateSource)
        {
            neteaseBoxWinRateSource.SetWinRateCaptureEnabled(enabled: false);
        }
    }

    public void OnActionExecutionSucceeded(ConstructedGameState state, ConstructedAction action)
    {
        if (state.Phase == ConstructedPhase.LocalTurn)
            _startupLocalActionSucceeded = true;
        if (action.Type == ConstructedActionType.ConfirmMulligan)
        {
            _mulliganSubmitted = true;
            _mulliganLease = null;
        }
        if (_enableMissingOptionsRecovery)
        {
            _missingOptionsRecovery.NotifyActionExecutionSucceeded(state, action);
        }

        string text = BuildRecommendationExecutionKey(state, action);
        if (text != null)
        {
            _lastExecutedRecommendationKey = text;
        }

        if (IsJail319Reroll(action) && action.Parameters.TryGetValue("netease.observationKey", out string value) && !string.IsNullOrWhiteSpace(value))
        {
            _lastExecutedJail319ObservationKey = value;
        }

        if (_recoveryMonitoringEnabled)
        {
            DateTimeOffset utcNow = _timeProvider.GetUtcNow();
            _recoveryState.MarkHealthy();
            _missingRecommendationSince = null;
            _noSuccessfulActionSince = ((state.Phase == ConstructedPhase.LocalTurn) ? new DateTimeOffset? (utcNow) : ((DateTimeOffset? )null));
            _boxRecoveryCooldownStartedAt = null;
        }
    }

    public void ObserveState(ConstructedGameState state)
    {
        // Navigation skips DecideAsync; still propagate confirmed boundaries.
        if (state.Phase is ConstructedPhase.Queue or ConstructedPhase.GameOver)
            _matchBoundaryObserved = true;
    }

    public async Task InitializeAsync(ConstructedStrategyRuntimeContext context, CancellationToken cancellationToken)
    {
        _hearthstoneProcessId = context.HearthstoneProcessId;
        _initialized = false;
        _hasObservedState = false;
        _lastPhase = ConstructedPhase.Unknown;
        _strategyMatchId = "";
        _matchBoundaryObserved = false;
        ResetStartupCapture();
        // Discovery may take seconds. Do not exclude recommendations captured
        // during initialization by moving the epoch to its completion time.
        SetGameEpoch(_timeProvider.GetUtcNow().AddSeconds(-2.0));
        await _source.InitializeAsync(context.HearthstoneProcessId, cancellationToken);
        _recoveryState.MarkHealthy();
        _initialized = true;
        Log?.Invoke("盒子AI传统对战策略已就绪；自动卡死重启默认关闭，开启后无论第几次都只重启盒子。");
    }

    public async Task<IReadOnlyList<ConstructedAction>> DecideAsync(ConstructedGameState state, ConstructedSettings settings, CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            return Wait("盒子AI策略尚未完成初始化");
        }

        bool hadObservedState = _hasObservedState;
        _hasObservedState = true;
        ObserveState(state);
        bool active = state.Phase is ConstructedPhase.Mulligan or ConstructedPhase.LocalTurn or
            ConstructedPhase.OpponentTurn or ConstructedPhase.Discover or ConstructedPhase.Choice;
        bool newMatch = active && (_matchBoundaryObserved ||
            (_strategyMatchId.Length > 0 && _strategyMatchId != state.MatchId));
        bool flag = state.Phase == ConstructedPhase.Mulligan &&
            (_lastPhase != ConstructedPhase.Mulligan || newMatch);
        if (state.Phase != ConstructedPhase.Unknown) _lastPhase = state.Phase;
        if (active) { _strategyMatchId = state.MatchId; _matchBoundaryObserved = false; }
        if (newMatch && !flag)
        {
            SetGameEpoch(_timeProvider.GetUtcNow().AddSeconds(-3));
            ResetStartupCapture();
            _lastOptionsPacketId = 0;
            _lastExecutedRecommendationKey = null;
            _lastExecutedJail319ObservationKey = null;
        }
        string text = NormalizeWinRateMatchId(state.MatchId);
        if (!settings.EnableNeteaseBoxLowWinRateAutoConcede)
        {
            ResetWinRate();
        }
        else if (flag || string.IsNullOrWhiteSpace(_winRateMatchId) || !string.Equals(_winRateMatchId, text, StringComparison.Ordinal))
        {
            _latestWinRate = null;
            _winRateMatchId = text;
            _winRateGameEpoch = _timeProvider.GetUtcNow().AddSeconds(-2.0);
            _winRateCaptureStarted = false;
        }

        if (settings.EnableNeteaseBoxLowWinRateAutoConcede && state.ManaTotal >= 4)
        {
            _winRateCaptureStarted = true;
        }

        if (_source is INeteaseBoxWinRateSource neteaseBoxWinRateSource)
        {
            neteaseBoxWinRateSource.SetWinRateCaptureEnabled(settings.EnableNeteaseBoxLowWinRateAutoConcede && _winRateCaptureStarted);
        }

        if (settings.EnableNeteaseBoxAutoRecovery)
        {
            _recoveryMonitoringEnabled = true;
            _recoveryMonitoringConfigurationInitialized = true;
        }
        else if (!_recoveryMonitoringConfigurationInitialized || _recoveryMonitoringEnabled)
        {
            DisableRecoveryMonitoring();
            _recoveryMonitoringEnabled = false;
            _recoveryMonitoringConfigurationInitialized = true;
        }

        if (settings.DryRun || state.Phase != ConstructedPhase.LocalTurn)
        {
            ResetLocalTurnTracking();
        }

        ConstructedPhase phase = state.Phase;
        if (((uint)(phase - 2) > 1u && (uint)(phase - 5) > 1u) || 1 == 0)
        {
            if (_enableMissingOptionsRecovery)
            {
                _missingOptionsRecovery.TryCreateRecoveryAction(state, ReadOptionsPacketId(state));
            }

            CaptureLatestWinRate(state, settings);
            QueueOpponentTurnWinRateRefresh();
            return Wait("等待进入起手、我方回合或选择阶段");
        }

        if (flag)
        {
            if (_enableMissingOptionsRecovery)
            {
                _missingOptionsRecovery.Reset();
            }

            if (hadObservedState)
                SetGameEpoch(_timeProvider.GetUtcNow().AddSeconds(-3.0));
            ResetStartupCapture();
            _lastOptionsPacketId = 0;
            _lastExecutedRecommendationKey = null;
            _lastExecutedJail319ObservationKey = null;
            _latestWinRate = null;
            _winRateMatchId = text;
            _winRateGameEpoch = _timeProvider.GetUtcNow().AddSeconds(-3.0);
            _winRateCaptureStarted = false;
            Log?.Invoke("检测到新一局起手阶段，已隔离此前对局的盒子AI推荐事件。");
        }

        if (state.Phase == ConstructedPhase.Mulligan && _mulliganSubmitted)
            return WaitWithStatus("起手换牌已提交，等待服务器发牌并进入首回合");

        int optionsPacketId = ReadOptionsPacketId(state);
        if (state.Phase == ConstructedPhase.LocalTurn && optionsPacketId <= 0)
        {
            ResetLocalTurnTracking();
            ConstructedAction constructedAction = (_enableMissingOptionsRecovery ? _missingOptionsRecovery.TryCreateRecoveryAction(state, optionsPacketId) : null);
            if ((object)constructedAction != null)
            {
                return new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(constructedAction);
            }

            return WaitWithStatus("等待客户端生成本手 OptionsPacket，暂不读取或执行旧推荐");
        }

        if (_enableMissingOptionsRecovery)
        {
            _missingOptionsRecovery.TryCreateRecoveryAction(state, optionsPacketId);
        }

        if (_requireManaSync && state.Phase == ConstructedPhase.LocalTurn && state.Turn > 0 && state.ManaTotal <= 0)
        {
            ResetLocalTurnTracking();
            return WaitWithStatus("等待回合开始费用同步完成，暂不读取或执行盒子推荐");
        }

        if (state.Phase == ConstructedPhase.LocalTurn && optionsPacketId > 0)
        {
            if (_lastOptionsPacketId > 0 && optionsPacketId < _lastOptionsPacketId)
            {
                SetGameEpoch(_timeProvider.GetUtcNow().AddSeconds(-3.0));
                Log?.Invoke("检测到新对局的 OptionsPacket 编号已重置，已清空旧推荐时间窗口。");
            }

            _lastOptionsPacketId = optionsPacketId;
        }

        if (settings.EnableNeteaseBoxAutoRecovery && state.Phase == ConstructedPhase.LocalTurn && !settings.DryRun)
        {
            TrackEligibleLocalTurn(state);
        }

        int desiredRecommendationOptionId = ((state.Phase == ConstructedPhase.LocalTurn) ? optionsPacketId : 0);
        string recommendationReadFailure = null;
        IReadOnlyList<NeteaseBoxRecommendationObservation> readOnlyList2;
        try
        {
            IReadOnlyList<NeteaseBoxRecommendationObservation> readOnlyList = ((state.Phase != ConstructedPhase.LocalTurn) ? (await _source.RefreshAsync(desiredRecommendationOptionId, cancellationToken)) : (await _source.ReadAsync(desiredRecommendationOptionId, cancellationToken)));
            readOnlyList2 = readOnlyList;
        }
        catch (NeteaseDirectAuthenticationException)
        {
            throw;
        }
        catch (Exception ex2)when (!(ex2 is OperationCanceledException))
        {
            recommendationReadFailure = "盒子AI推荐读取失败：" + ex2.Message;
            readOnlyList2 = Array.Empty<NeteaseBoxRecommendationObservation>();
        }

        readOnlyList2 = await PrepareStartupRecommendationsAsync(state, desiredRecommendationOptionId, readOnlyList2, cancellationToken);
        CaptureLatestWinRate(state, settings);
        NeteaseBoxRecommendationObservation[] array = ((state.Phase == ConstructedPhase.LocalTurn) ? readOnlyList2.Where((NeteaseBoxRecommendationObservation item) => item.CapturedAt >= _gameEpoch && item.Payload.OptionId == optionsPacketId).ToArray() : Array.Empty<NeteaseBoxRecommendationObservation>());
        bool hasCurrentLocalRecommendation = array.Any(HasRecommendedPlay);
        if (readOnlyList2.Count == 0)
        {
            ConstructedAction constructedAction2 = await TryRecoverLocalTurnStallAsync(state, hasCurrentLocalRecommendation, settings, cancellationToken);
            if ((object)constructedAction2 != null)
            {
                return new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(constructedAction2);
            }

            string reason = recommendationReadFailure ?? ((state.Phase == ConstructedPhase.Mulligan) ? "等待盒子AI起手推荐" : $"等待盒子AI针对 optionId={optionsPacketId} 的新推荐");
            return (state.Phase == ConstructedPhase.LocalTurn) ? WaitWithStallStatus(reason, "Recommendation", settings.EnableNeteaseStallConcedeProtection) : WaitWithStatus(reason);
        }

        NeteaseBoxRecommendationObservation[] candidateObservations = ((state.Phase == ConstructedPhase.LocalTurn) ? array : readOnlyList2.Where((NeteaseBoxRecommendationObservation item) => item.CapturedAt >= _gameEpoch).ToArray());
        bool refreshedIncompleteTarget = false;
        for (int pass = 0; pass < 2; pass++)
        {
            List<string> failures = new List<string>();
            bool flag2 = false;
            foreach (NeteaseBoxRecommendationObservation item in candidateObservations.OrderByDescending(RecommendationSpecificity).ThenByDescending((NeteaseBoxRecommendationObservation item) => item.CapturedAt))
            {
                if (TryMapNextInstruction(state, item.Payload, out IReadOnlyList<ConstructedAction> actions, out string reason2, out bool requiresTargetRefresh, out int instructionIndex))
                {
                    if (actions.Any(IsJail319Reroll))
                    {
                        string text2 = BuildObservationKey(item);
                        if (string.Equals(text2, _lastExecutedJail319ObservationKey, StringComparison.Ordinal))
                        {
                            return WaitWithStatus("等待盒子AI给出万能钥匙刷新后的新发现推荐");
                        }

                        actions = AddObservationKey(actions, text2);
                    }

                    LogStatus($"盒子AI optionId={item.Payload.OptionId} 第 {instructionIndex + 1} 条指令已唯一匹配客户端合法动作");
                    return actions;
                }

                if (!refreshedIncompleteTarget & requiresTargetRefresh)
                {
                    flag2 = true;
                }
                else if (!string.IsNullOrWhiteSpace(reason2))
                {
                    failures.Add(reason2);
                }
            }

            if (!refreshedIncompleteTarget & flag2)
            {
                refreshedIncompleteTarget = true;
                LogStatus($"盒子AI optionId={optionsPacketId} 的当前事件尚缺目标，立即刷新同一手完整内存事件");
                IReadOnlyList<NeteaseBoxRecommendationObservation> readOnlyList3;
                try
                {
                    readOnlyList3 = await _source.RefreshAsync(desiredRecommendationOptionId, cancellationToken);
                }
                catch (NeteaseDirectAuthenticationException)
                {
                    throw;
                }
                catch (Exception ex4)when (settings.EnableNeteaseStallConcedeProtection && !(ex4 is OperationCanceledException))
                {
                    return WaitWithStallStatus("盒子AI刷新当前推荐失败：" + ex4.Message, "Recommendation", enabled: true);
                }

                if (readOnlyList3.Count > 0)
                {
                    readOnlyList2 = readOnlyList3;
                    array = ((state.Phase == ConstructedPhase.LocalTurn) ? readOnlyList2.Where((NeteaseBoxRecommendationObservation item) => item.CapturedAt >= _gameEpoch && item.Payload.OptionId == optionsPacketId).ToArray() : Array.Empty<NeteaseBoxRecommendationObservation>());
                    hasCurrentLocalRecommendation = array.Any(HasRecommendedPlay);
                    candidateObservations = ((state.Phase == ConstructedPhase.LocalTurn) ? array : readOnlyList2.Where((NeteaseBoxRecommendationObservation item) => item.CapturedAt >= _gameEpoch).ToArray());
                }

                continue;
            }

            ConstructedAction constructedAction3 = await TryRecoverLocalTurnStallAsync(state, hasCurrentLocalRecommendation, settings, cancellationToken);
            if ((object)constructedAction3 != null)
            {
                return new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(constructedAction3);
            }

            return WaitWithStallStatus(failures.FirstOrDefault() ?? $"盒子AI当前推荐尚不能与 optionId={optionsPacketId} 唯一对应", "Recommendation", settings.EnableNeteaseStallConcedeProtection);
        }

        ConstructedAction constructedAction4 = await TryRecoverLocalTurnStallAsync(state, hasCurrentLocalRecommendation, settings, cancellationToken);
        if ((object)constructedAction4 != null)
        {
            return new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(constructedAction4);
        }

        return WaitWithStallStatus($"盒子AI当前推荐尚不能与 optionId={optionsPacketId} 唯一对应", "Recommendation", settings.EnableNeteaseStallConcedeProtection);
    }

    private void SetGameEpoch(DateTimeOffset epoch)
    {
        _gameEpoch = epoch;
        _source.SetRecommendationEpoch(epoch);
    }

    private void ResetStartupCapture()
    {
        _startupLocalActionSucceeded = false;
        _mulliganSubmitted = false;
        _startupRelocations = 0;
        _startupMissingSince = null;
        _mulliganLease = null;
        _mulliganLeaseScope = "";
    }

    private IReadOnlyList<NeteaseBoxRecommendationObservation> RetainCurrentMulligan(
        ConstructedGameState state, IReadOnlyList<NeteaseBoxRecommendationObservation> observations)
    {
        var now = _timeProvider.GetUtcNow();
        string scope = state.Phase == ConstructedPhase.Mulligan
            ? state.MatchId + "|" + string.Join(";", state.Hand.OrderBy(c => c.EntityId, StringComparer.Ordinal)
                .Select(c => c.EntityId + ":" + c.CardId)) : "";
        if (scope != _mulliganLeaseScope || state.Phase != ConstructedPhase.Mulligan || now >= _mulliganLeaseUntil)
            _mulliganLease = null;
        _mulliganLeaseScope = scope;
        if (state.Phase != ConstructedPhase.Mulligan || _mulliganSubmitted) return observations;

        // Only a fresh event that already maps uniquely to this exact hand gets
        // a lease. Preserve its original timestamp; never extend on each poll.
        var fresh = observations.Where(o => o.CapturedAt != DateTimeOffset.MinValue && o.CapturedAt >= _gameEpoch &&
                o.CapturedAt >= now.AddSeconds(-20) && o.CapturedAt <= now.AddSeconds(2))
            .OrderByDescending(o => o.CapturedAt)
            .FirstOrDefault(o => TryMapNextInstruction(state, o.Payload, out _, out _, out _, out _));
        if (fresh != null && (_mulliganLease == null || fresh.CapturedAt > _mulliganLease.CapturedAt))
        {
            _mulliganLease = fresh;
            _mulliganLeaseUntil = now.AddSeconds(60);
        }
        if (_mulliganLease == null || _mulliganLease.CapturedAt < _gameEpoch) return observations;
        return observations.Append(_mulliganLease).Distinct().ToArray();
    }

    private async Task<IReadOnlyList<NeteaseBoxRecommendationObservation>> PrepareStartupRecommendationsAsync(
        ConstructedGameState state, int desiredOptionId,
        IReadOnlyList<NeteaseBoxRecommendationObservation> observations, CancellationToken token)
    {
        observations = RetainCurrentMulligan(state, observations);
        bool startup = (state.Phase == ConstructedPhase.Mulligan && !_mulliganSubmitted) ||
            (state.Phase == ConstructedPhase.LocalTurn && state.Turn <= 1 && !_startupLocalActionSucceeded);
        bool usable = observations.Any(o => o.CapturedAt != DateTimeOffset.MinValue && o.CapturedAt >= _gameEpoch &&
            (state.Phase != ConstructedPhase.LocalTurn || o.Payload.OptionId == desiredOptionId) &&
            TryMapNextInstruction(state, o.Payload, out _, out _, out _, out _));
        if (!startup || usable) { _startupMissingSince = null; return observations; }

        var now = _timeProvider.GetUtcNow();
        _startupMissingSince ??= now;
        if (_startupRelocations >= 2 || now - _startupMissingSince.Value < TimeSpan.FromSeconds(5))
            return observations;
        _startupRelocations++;
        _startupMissingSince = now;
        try
        {
            token.ThrowIfCancellationRequested();
            if (await _source.RelocateAsync(token))
            {
                Log?.Invoke("开局建议尚未就绪，已重新定位盒子建议读取模块；无需停止再开始。");
                var refreshed = await _source.RefreshAsync(desiredOptionId, token);
                return RetainCurrentMulligan(state, refreshed);
            }
        }
        catch (NeteaseDirectAuthenticationException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log?.Invoke("开局建议读取重新定位失败，继续等待当前对局：" + ex.Message);
        }
        return observations;
    }

    private void CaptureLatestWinRate(ConstructedGameState state, ConstructedSettings settings)
    {
        if (!settings.EnableNeteaseBoxLowWinRateAutoConcede || !_winRateCaptureStarted || !(_source is INeteaseBoxWinRateSource neteaseBoxWinRateSource))
        {
            return;
        }

        int currentTurn = Math.Max(1, state.Turn);
        NeteaseBoxWinRateObservation neteaseBoxWinRateObservation = (
            from item in neteaseBoxWinRateSource.ReadCachedWinRates().Where((NeteaseBoxWinRateObservation item) =>
            {
                if (item.CapturedAt >= _winRateGameEpoch && double.IsFinite(item.Payload.MyWinRate))
                {
                    double myWinRate = item.Payload.MyWinRate;
                    if (myWinRate >= 0.0)
                    {
                        return myWinRate <= 100.0;
                    }

                    return false;
                }

                return false;
            })orderby Math.Abs((item.Payload.Turns.LastOrDefault()?.TurnId ?? currentTurn) - currentTurn)select item).ThenByDescending((NeteaseBoxWinRateObservation item) => item.Payload.Turns.LastOrDefault()?.Timestamp ?? "", StringComparer.Ordinal).ThenByDescending((NeteaseBoxWinRateObservation item) => item.Payload.Turns.Count).ThenByDescending((NeteaseBoxWinRateObservation item) => item.CapturedAt).ThenByDescending((NeteaseBoxWinRateObservation item) => item.Payload.Turns.LastOrDefault()?.TurnId ?? 0).FirstOrDefault();
        if ((object)neteaseBoxWinRateObservation != null)
        {
            _latestWinRate = new ConstructedWinRateSnapshot(_winRateMatchId = NormalizeWinRateMatchId(state.MatchId), neteaseBoxWinRateObservation.Payload.MyWinRate, neteaseBoxWinRateObservation.CapturedAt, neteaseBoxWinRateObservation.Payload.Turns.LastOrDefault()?.TurnId ?? 0);
        }
    }

    private void QueueOpponentTurnWinRateRefresh()
    {
        if (_winRateCaptureStarted && _source is INeteaseBoxWinRateSource winRateSource)
        {
            Task opponentTurnWinRateRefreshTask = _opponentTurnWinRateRefreshTask;
            if (opponentTurnWinRateRefreshTask == null || opponentTurnWinRateRefreshTask.IsCompleted)
            {
                _opponentTurnWinRateRefreshTask = RefreshOpponentTurnWinRateAsync(winRateSource);
            }
        }
    }

    private async Task RefreshOpponentTurnWinRateAsync(INeteaseBoxWinRateSource winRateSource)
    {
        try
        {
            await winRateSource.RefreshWinRatesAsync(CancellationToken.None);
        }
        catch (Exception ex)when (!(ex is OperationCanceledException))
        {
            LogStatus("盒子AI胜率后台读取失败：" + ex.Message);
        }
    }

    private static string NormalizeWinRateMatchId(string matchId)
    {
        if (!matchId.StartsWith("constructed-", StringComparison.OrdinalIgnoreCase))
        {
            return matchId;
        }

        int num = matchId.LastIndexOf('-');
        if (num <= "constructed-".Length)
        {
            return matchId;
        }

        return matchId.Substring(0, num);
    }

    private async Task<ConstructedAction?> TryRecoverLocalTurnStallAsync(ConstructedGameState state, bool hasCurrentRecommendation, ConstructedSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.EnableNeteaseBoxAutoRecovery || state.Phase != ConstructedPhase.LocalTurn || !_noSuccessfulActionSince.HasValue)
        {
            return null;
        }

        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        if (hasCurrentRecommendation)
        {
            _missingRecommendationSince = null;
        }
        else
        {
            _missingRecommendationSince.GetValueOrDefault();
            if (!_missingRecommendationSince.HasValue)
            {
                DateTimeOffset value = utcNow;
                _missingRecommendationSince = value;
            }
        }

        TimeSpan timeSpan = (_missingRecommendationSince.HasValue ? (utcNow - _missingRecommendationSince.Value) : TimeSpan.Zero);
        TimeSpan timeSpan2 = utcNow - _noSuccessfulActionSince.Value;
        TimeSpan timeSpan3 = TimeSpan.FromSeconds(BotSettingsPolicy.NormalizeNeteaseBoxStallTimeoutSeconds(settings.NeteaseBoxStallTimeoutSeconds));
        bool flag = _missingRecommendationSince.HasValue && timeSpan >= timeSpan3;
        bool flag2 = timeSpan2 >= timeSpan3;
        if (!flag && !flag2)
        {
            return null;
        }

        List<string> list = new List<string>(2);
        if (flag)
        {
            list.Add($"当前 optionId={ReadOptionsPacketId(state)} 连续 {timeSpan.TotalSeconds:0} 秒无有效推荐打法");
        }

        if (flag2)
        {
            list.Add($"连续 {timeSpan2.TotalSeconds:0} 秒没有成功执行任何操作");
        }

        string value2 = "我方回合判定盒子AI卡住：" + string.Join("；", list);
        TimeSpan recoveryCooldown = TimeSpan.FromSeconds(BotSettingsPolicy.NormalizeNeteaseBoxRecoveryCooldownSeconds(settings.NeteaseBoxRecoveryCooldownSeconds));
        DateTimeOffset? dateTimeOffset = _boxRecoveryCooldownStartedAt + recoveryCooldown;
        if (dateTimeOffset.HasValue && utcNow < dateTimeOffset.Value)
        {
            TimeSpan timeSpan4 = dateTimeOffset.Value - utcNow;
            string text = $"盒子AI盒子重启恢复冷却中，至少再等待 {Math.Ceiling(timeSpan4.TotalSeconds):0} 秒后才允许下一次恢复";
            LogStatus(text);
            return CreateOptionalStallWaitAction(text, "Recommendation", settings.EnableNeteaseStallConcedeProtection);
        }

        int attempt = _recoveryState.BeginRecoveryAttempt();
        bool flag3 = await _source.RestartAndReconnectAsync(_hearthstoneProcessId, $"{value2}；执行第 {attempt} 次盒子单独重启，永不升级为炉石或脚本重启", cancellationToken);
        DateTimeOffset utcNow2 = _timeProvider.GetUtcNow();
        ResetLocalTurnRecoveryWindow(utcNow2);
        _boxRecoveryCooldownStartedAt = utcNow2;
        string text2 = (flag3 ? $"盒子AI已完成第 {attempt} 次盒子重启并重新绑定，进入 {recoveryCooldown.TotalSeconds:0} 秒恢复冷却并等待当前对局新推荐；后续仍只重启盒子" : $"盒子AI第 {attempt} 次盒子重启未完成，进入 {recoveryCooldown.TotalSeconds:0} 秒恢复冷却；后续仍只重启盒子");
        LogStatus(text2);
        return CreateOptionalStallWaitAction(text2, "Recommendation", settings.EnableNeteaseStallConcedeProtection);
    }

    private void TrackEligibleLocalTurn(ConstructedGameState state)
    {
        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        LocalTurnProgressSnapshot localTurnProgressSnapshot = CreateLocalTurnProgressSnapshot(state);
        if (_noSuccessfulActionSince.HasValue && _trackedLocalTurn == state.Turn && string.Equals(_trackedLocalMatchId, state.MatchId, StringComparison.Ordinal))
        {
            if (!_lastLocalTurnProgress.HasValue || _lastLocalTurnProgress.Value != localTurnProgressSnapshot)
            {
                _lastLocalTurnProgress = localTurnProgressSnapshot;
                _missingRecommendationSince = utcNow;
                _noSuccessfulActionSince = utcNow;
            }
        }
        else
        {
            _trackedLocalMatchId = state.MatchId;
            _trackedLocalTurn = state.Turn;
            _lastLocalTurnProgress = localTurnProgressSnapshot;
            _missingRecommendationSince = utcNow;
            _noSuccessfulActionSince = utcNow;
        }
    }

    private void ResetLocalTurnRecoveryWindow(DateTimeOffset? resetAt = null)
    {
        DateTimeOffset value = resetAt ?? _timeProvider.GetUtcNow();
        _missingRecommendationSince = value;
        _noSuccessfulActionSince = value;
    }

    private void ResetLocalTurnTracking()
    {
        _trackedLocalMatchId = "";
        _trackedLocalTurn = 0;
        _lastLocalTurnProgress = null;
        _missingRecommendationSince = null;
        _noSuccessfulActionSince = null;
    }

    private void DisableRecoveryMonitoring()
    {
        ResetLocalTurnTracking();
        _boxRecoveryCooldownStartedAt = null;
        _recoveryState.MarkHealthy();
    }

    private static LocalTurnProgressSnapshot CreateLocalTurnProgressSnapshot(ConstructedGameState state)
    {
        return new LocalTurnProgressSnapshot(ReadOptionsPacketId(state), state.ManaAvailable, state.ManaTotal, state.HeroHealth, state.HeroArmor, state.OpponentHealth, state.OpponentArmor, BuildEntitySignature(state.LocalHero), BuildEntitySignature(state.OpponentHero), BuildEntityCollectionSignature(state.Hand), BuildEntityCollectionSignature(state.FriendlyBoard), BuildEntityCollectionSignature(state.EnemyBoard), BuildEntitySignature(state.FriendlyWeapon), BuildEntitySignature(state.EnemyWeapon), BuildEntityCollectionSignature(state.GetLocalHeroPowers()), BuildActionCollectionSignature(state.CurrentPlayableActions), state.SecretsCount);
    }

    private static int BuildEntityCollectionSignature(IReadOnlyList<ConstructedEntity> entities)
    {
        int num = 0;
        int num2 = 0;
        foreach (ConstructedEntity entity in entities)
        {
            int num3 = BuildEntitySignature(entity);
            num += num3;
            num2 ^= num3;
        }

        return HashCode.Combine(entities.Count, num, num2);
    }

    private static int BuildEntitySignature(ConstructedEntity? entity)
    {
        if ((object)entity == null)
        {
            return 0;
        }

        HashCode hashCode = default;
        hashCode.Add(entity.EntityId, StringComparer.Ordinal);
        hashCode.Add(entity.CardId, StringComparer.Ordinal);
        hashCode.Add(entity.Zone);
        hashCode.Add(entity.Cost);
        hashCode.Add(entity.Attack);
        hashCode.Add(entity.Health);
        hashCode.Add(entity.Damage);
        hashCode.Add(entity.CanAttack);
        hashCode.Add(entity.HasTaunt);
        hashCode.Add(entity.HasDivineShield);
        hashCode.Add(entity.HasRush);
        hashCode.Add(entity.HasCharge);
        hashCode.Add(entity.HasStealth);
        hashCode.Add(entity.CantBeTargetedBySpellsOrHeroPowers);
        hashCode.Add(entity.HasImmune);
        hashCode.Add(entity.SpellDamage);
        hashCode.Add(entity.LocationDurability);
        hashCode.Add(entity.LocationCooldown);
        return hashCode.ToHashCode();
    }

    private static int BuildActionCollectionSignature(IReadOnlyList<ConstructedAction> actions)
    {
        int num = 0;
        int num2 = 0;
        foreach (ConstructedAction action in actions)
        {
            HashCode hashCode = default;
            hashCode.Add(action.Type);
            hashCode.Add(action.SourceEntityId, StringComparer.Ordinal);
            hashCode.Add(action.TargetEntityId, StringComparer.Ordinal);
            hashCode.Add(action.OptionIndex);
            hashCode.Add(action.ExpectedCost);
            int num3 = hashCode.ToHashCode();
            num += num3;
            num2 ^= num3;
        }

        return HashCode.Combine(actions.Count, num, num2);
    }

    private static bool HasRecommendedPlay(NeteaseBoxRecommendationObservation observation)
    {
        return observation.Payload.Data.Any((NeteaseBoxRecommendedAction item) => !string.IsNullOrWhiteSpace(item.ActionName));
    }

    private bool TryMapNextInstruction(ConstructedGameState state, NeteaseBoxRecommendation recommendation, out IReadOnlyList<ConstructedAction> actions, out string reason, out bool requiresTargetRefresh, out int instructionIndex)
    {
        actions = Array.Empty<ConstructedAction>();
        reason = "";
        requiresTargetRefresh = false;
        instructionIndex = -1;
        List<string> list = new List<string>();
        bool flag = false;
        for (int i = 0; i < recommendation.Data.Count; i++)
        {
            NeteaseBoxRecommendedAction neteaseBoxRecommendedAction = recommendation.Data[i];
            NeteaseBoxRecommendation recommendation2 = recommendation with
            {
                Data = new NeteaseBoxRecommendedAction[1]
                {
                    neteaseBoxRecommendedAction
                }
            };
            if (!NeteaseBoxInstructionMapper.TryMapFirst(state, recommendation2, out IReadOnlyList<ConstructedAction> actions2, out string reason2, out bool requiresTargetRefresh2, _enableDirectCompatibility))
            {
                if (requiresTargetRefresh2)
                {
                    requiresTargetRefresh = true;
                }

                if (!string.IsNullOrWhiteSpace(reason2))
                {
                    list.Add(reason2);
                }

                continue;
            }

            actions2 = AddInstructionIndex(actions2, i);
            string text = (
                from action in actions2
                where action.Type != ConstructedActionType.Wait
                select BuildRecommendationExecutionKey(state, action)).FirstOrDefault((string key) => key != null);
            if (text != null && string.Equals(text, _lastExecutedRecommendationKey, StringComparison.Ordinal))
            {
                list.Add($"盒子AI optionId={recommendation.OptionId} 第 {i + 1} 条指令已经成功执行，继续检查同一事件的下一条指令");
                continue;
            }

            if (actions2.Any(IsSourceOnlySelection))
            {
                flag = true;
                continue;
            }

            actions = actions2;
            instructionIndex = i;
            reason = "";
            return true;
        }

        if (flag)
        {
            requiresTargetRefresh = true;
            list.Add($"盒子AI optionId={recommendation.OptionId} 当前指令尚缺目标");
        }

        reason = list.FirstOrDefault() ?? "盒子AI尚未返回可执行指令";
        return false;
    }

    private static IReadOnlyList<ConstructedAction> AddInstructionIndex(IReadOnlyList<ConstructedAction> actions, int instructionIndex)
    {
        string parametersValue = instructionIndex.ToString(CultureInfo.InvariantCulture);
        return actions.Select((ConstructedAction action) =>
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>(action.Parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["netease.instructionIndex"] = parametersValue
            };
            return action with
            {
                Parameters = parameters
            };
        }).ToArray();
    }

    private static IReadOnlyList<ConstructedAction> AddObservationKey(IReadOnlyList<ConstructedAction> actions, string observationKey)
    {
        return actions.Select((ConstructedAction action) => action with { Parameters = new Dictionary<string, string>(action.Parameters, StringComparer.OrdinalIgnoreCase) { ["netease.observationKey"] = observationKey } }).ToArray();
    }

    private static string BuildObservationKey(NeteaseBoxRecommendationObservation observation)
    {
        return string.Join("|", observation.CapturedAt.ToString("O", CultureInfo.InvariantCulture), observation.RendererProcessId.ToString(CultureInfo.InvariantCulture), observation.Address.ToString("X", CultureInfo.InvariantCulture));
    }

    private static bool IsJail319Reroll(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.Parameters.TryGetValue("hsauto.jail319Reroll", out value) && bool.TryParse(value, out result)) & result;
    }

    private static string? BuildRecommendationExecutionKey(ConstructedGameState state, ConstructedAction action)
    {
        if (!action.Parameters.TryGetValue("netease.optionId", out string value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) || result <= 0)
        {
            return null;
        }

        action.Parameters.TryGetValue("netease.actionName", out string value2);
        action.Parameters.TryGetValue("unity.selectedSubOptionIndex", out string value3);
        action.Parameters.TryGetValue("netease.instructionIndex", out string value4);
        return string.Join("|", state.MatchId, state.Turn.ToString(CultureInfo.InvariantCulture), result.ToString(CultureInfo.InvariantCulture), value2 ?? "", action.Type.ToString(), action.SourceEntityId ?? "", action.TargetEntityId ?? "", action.OptionIndex?.ToString(CultureInfo.InvariantCulture) ?? "", value3 ?? "", value4 ?? "");
    }

    private static int RecommendationSpecificity(NeteaseBoxRecommendationObservation observation)
    {
        if (observation.Payload.Data.Count == 0)
        {
            return 0;
        }

        if ((object)observation.Payload.Data.FirstOrDefault((NeteaseBoxRecommendedAction item) => HasTargetMarker(item.Target) || HasTargetMarker(item.OppTarget) || HasTargetMarker(item.TargetHero) || HasTargetMarker(item.OppTargetHero) || HasTargetMarker(item.HyphenTarget) || HasTargetMarker(item.HyphenOppTarget) || HasTargetMarker(item.HyphenTargetHero) || HasTargetMarker(item.HyphenOppTargetHero)) == null)
        {
            return observation.Payload.Data.Any((NeteaseBoxRecommendedAction item) => HasObject(item.Card)) ? 1 : 0;
        }

        return 2;
    }

    private static bool HasTargetMarker(JsonElement element)
    {
        long value;
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Number => !element.TryGetInt64(out value) || value != 0,
            JsonValueKind.True => true,
            _ => false,
        };
    }

    private static bool IsSourceOnlySelection(ConstructedAction action)
    {
        string value;
        bool result = default;
        return (action.Parameters.TryGetValue("netease.selectSourceOnly", out value) && bool.TryParse(value, out result)) & result;
    }

    private static bool HasObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return element.EnumerateObject().Any();
        }

        return false;
    }

    private IReadOnlyList<ConstructedAction> WaitWithStatus(string reason)
    {
        LogStatus(reason);
        return Wait(reason);
    }

    private IReadOnlyList<ConstructedAction> WaitWithStallStatus(string reason, string stallKind, bool enabled)
    {
        LogStatus(reason);
        return new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(CreateOptionalStallWaitAction(reason, stallKind, enabled));
    }

    private void LogStatus(string status)
    {
        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        if (!string.Equals(status, _lastStatus, StringComparison.Ordinal) || utcNow - _lastStatusAt >= TimeSpan.FromSeconds(8.0))
        {
            _lastStatus = status;
            _lastStatusAt = utcNow;
            Log?.Invoke(status);
        }
    }

    private static int ReadOptionsPacketId(ConstructedGameState state)
    {
        if (!state.Tags.TryGetValue("unity.optionsPacketId", out string value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }

    private static IReadOnlyList<ConstructedAction> Wait(string reason)
    {
        return new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(new ConstructedAction { Type = ConstructedActionType.Wait, Reason = reason });
    }

    internal static ConstructedAction CreateStallWaitAction(string reason, string stallKind)
    {
        return new ConstructedAction
        {
            Type = ConstructedActionType.Wait,
            Reason = reason,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hsauto.neteaseConstructedStallKind"] = stallKind
            }
        };
    }

    private static ConstructedAction CreateOptionalStallWaitAction(string reason, string stallKind, bool enabled)
    {
        if (!enabled)
        {
            return new ConstructedAction
            {
                Type = ConstructedActionType.Wait,
                Reason = reason
            };
        }

        return CreateStallWaitAction(reason, stallKind);
    }
}
