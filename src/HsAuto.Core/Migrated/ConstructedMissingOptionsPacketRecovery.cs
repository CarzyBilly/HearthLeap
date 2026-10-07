// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using HsAuto.Core.Models;

namespace HsAuto.Core.Strategy;
internal sealed class ConstructedMissingOptionsPacketRecovery
{
    internal static readonly TimeSpan CleanupDelay = TimeSpan.FromSeconds(5.0);
    private readonly TimeProvider _timeProvider;
    private readonly string _strategyDisplayName;
    private DateTimeOffset _missingOptionsSince = DateTimeOffset.MinValue;
    private DateTimeOffset _lastCleanupRequestedAt = DateTimeOffset.MinValue;
    private ConstructedActionType _lastSuccessfulActionType = ConstructedActionType.Wait;
    private int _lastSuccessfulOptionsPacketId;
    internal ConstructedActionType LastSuccessfulActionType => _lastSuccessfulActionType;

    public ConstructedMissingOptionsPacketRecovery(TimeProvider timeProvider, string strategyDisplayName)
    {
        _timeProvider = timeProvider;
        _strategyDisplayName = strategyDisplayName;
    }

    public void NotifyActionExecutionSucceeded(ConstructedGameState state, ConstructedAction action)
    {
        if (action.Type != ConstructedActionType.CancelPendingInput)
        {
            _lastSuccessfulActionType = action.Type;
            _lastSuccessfulOptionsPacketId = ReadOptionsPacketId(state);
            _missingOptionsSince = DateTimeOffset.MinValue;
            _lastCleanupRequestedAt = DateTimeOffset.MinValue;
        }
    }

    public ConstructedAction? TryCreateRecoveryAction(ConstructedGameState state, int optionsPacketId)
    {
        if (state.Phase != ConstructedPhase.LocalTurn)
        {
            _missingOptionsSince = DateTimeOffset.MinValue;
            _lastCleanupRequestedAt = DateTimeOffset.MinValue;
            ConstructedPhase phase = state.Phase;
            if ((uint)(phase - 5) > 1u)
            {
                _lastSuccessfulActionType = ConstructedActionType.Wait;
                _lastSuccessfulOptionsPacketId = 0;
            }

            return null;
        }

        if (optionsPacketId > 0)
        {
            _missingOptionsSince = DateTimeOffset.MinValue;
            _lastCleanupRequestedAt = DateTimeOffset.MinValue;
            if (_lastSuccessfulActionType != ConstructedActionType.Wait && (_lastSuccessfulOptionsPacketId <= 0 || optionsPacketId != _lastSuccessfulOptionsPacketId))
            {
                _lastSuccessfulActionType = ConstructedActionType.Wait;
                _lastSuccessfulOptionsPacketId = 0;
            }

            return null;
        }

        if (!CanRecoverAfter(_lastSuccessfulActionType))
        {
            return null;
        }

        DateTimeOffset utcNow = _timeProvider.GetUtcNow();
        if (_missingOptionsSince == DateTimeOffset.MinValue)
        {
            _missingOptionsSince = utcNow;
            return null;
        }

        if (utcNow - _missingOptionsSince < CleanupDelay || utcNow - _lastCleanupRequestedAt < CleanupDelay)
        {
            return null;
        }

        _lastCleanupRequestedAt = utcNow;
        return new ConstructedAction
        {
            Type = ConstructedActionType.CancelPendingInput,
            Reason = _strategyDisplayName + " 提交后 5 秒仍未生成新 OptionsPacket，清理客户端挂起输入"
        };
    }

    public void Reset()
    {
        _missingOptionsSince = DateTimeOffset.MinValue;
        _lastCleanupRequestedAt = DateTimeOffset.MinValue;
        _lastSuccessfulActionType = ConstructedActionType.Wait;
        _lastSuccessfulOptionsPacketId = 0;
    }

    private static bool CanRecoverAfter(ConstructedActionType actionType)
    {
        bool flag;
        switch (actionType)
        {
            case ConstructedActionType.NoOp:
            case ConstructedActionType.Wait:
            case ConstructedActionType.ConfirmMulligan:
            case ConstructedActionType.EndTurn:
            case ConstructedActionType.Concede:
                flag = true;
                break;
            default:
                flag = false;
                break;
        }

        return !flag;
    }

    private static int ReadOptionsPacketId(ConstructedGameState state)
    {
        if (!state.Tags.TryGetValue("unity.optionsPacketId", out string value) || !int.TryParse(value, out var result))
        {
            return 0;
        }

        return result;
    }
}