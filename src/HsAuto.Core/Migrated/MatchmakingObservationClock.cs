// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Automation;
public sealed class MatchmakingObservationClock
{
    private static readonly TimeSpan PendingStartLifetime = TimeSpan.FromSeconds(45.0);
    private static readonly TimeSpan TransitionCarryLifetime = TimeSpan.FromSeconds(45.0);
    private DateTimeOffset _pendingStartRequestedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _activeStartedAt = DateTimeOffset.MinValue;
    private int _activePopupInstanceId;
    private DateTimeOffset _carriedStartedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _carryUntil = DateTimeOffset.MinValue;
    public bool IsMatching => _activePopupInstanceId != 0;

    public bool HasRecentPendingStart(DateTimeOffset now)
    {
        return IsRecentPendingStart(now);
    }

    public void MarkMatchStartRequested(DateTimeOffset now)
    {
        _pendingStartRequestedAt = now;
    }

    public void Reset()
    {
        _pendingStartRequestedAt = DateTimeOffset.MinValue;
        _activeStartedAt = DateTimeOffset.MinValue;
        _activePopupInstanceId = 0;
        _carriedStartedAt = DateTimeOffset.MinValue;
        _carryUntil = DateTimeOffset.MinValue;
    }

    public TimeSpan ObserveMatching(int popupInstanceId, TimeSpan bridgeElapsed, DateTimeOffset now)
    {
        int num = ((popupInstanceId == 0) ? (-1) : popupInstanceId);
        DateTimeOffset dateTimeOffset = now - ((bridgeElapsed < TimeSpan.Zero) ? TimeSpan.Zero : bridgeElapsed);
        if (_activePopupInstanceId != num || _activeStartedAt == DateTimeOffset.MinValue)
        {
            _activePopupInstanceId = num;
            ref DateTimeOffset activeStartedAt = ref _activeStartedAt;
            DateTimeOffset dateTimeOffset2;
            if (_carriedStartedAt != DateTimeOffset.MinValue && now <= _carryUntil)
            {
                dateTimeOffset2 = Earlier(_carriedStartedAt, dateTimeOffset);
            }
            else
            {
                dateTimeOffset2 = (IsRecentPendingStart(now) ? Earlier(_pendingStartRequestedAt, dateTimeOffset) : dateTimeOffset);
            }

            activeStartedAt = dateTimeOffset2;
            _pendingStartRequestedAt = DateTimeOffset.MinValue;
            _carriedStartedAt = DateTimeOffset.MinValue;
            _carryUntil = DateTimeOffset.MinValue;
        }
        else if (dateTimeOffset < _activeStartedAt)
        {
            _activeStartedAt = dateTimeOffset;
        }

        if (!(now > _activeStartedAt))
        {
            return TimeSpan.Zero;
        }

        return now - _activeStartedAt;
    }

    public void ObserveNotMatching(DateTimeOffset now)
    {
        if (_activeStartedAt != DateTimeOffset.MinValue)
        {
            _carriedStartedAt = _activeStartedAt;
            _carryUntil = now.Add(TransitionCarryLifetime);
        }

        _activePopupInstanceId = 0;
        _activeStartedAt = DateTimeOffset.MinValue;
        if (!IsRecentPendingStart(now))
        {
            _pendingStartRequestedAt = DateTimeOffset.MinValue;
        }
    }

    private bool IsRecentPendingStart(DateTimeOffset now)
    {
        if (_pendingStartRequestedAt != DateTimeOffset.MinValue && _pendingStartRequestedAt <= now)
        {
            return now - _pendingStartRequestedAt <= PendingStartLifetime;
        }

        return false;
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right)
    {
        if (!(left <= right))
        {
            return right;
        }

        return left;
    }
}