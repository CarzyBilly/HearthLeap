// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Automation;
public sealed class MenuNavigationStallTracker
{
    public const int DefaultAttemptLimit = 15;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60.0);
    private readonly TimeSpan _timeout;
    private readonly int _attemptLimit;
    private DateTimeOffset _firstAttemptAt = DateTimeOffset.MinValue;
    private int _attemptCount;
    public int AttemptCount => _attemptCount;

    public MenuNavigationStallTracker(TimeSpan? timeout = null, int attemptLimit = 15)
    {
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException("timeout");
        }

        if (attemptLimit < 1)
        {
            throw new ArgumentOutOfRangeException("attemptLimit");
        }

        _attemptLimit = attemptLimit;
    }

    public TimeSpan Elapsed(DateTimeOffset now)
    {
        if (!(_firstAttemptAt == DateTimeOffset.MinValue))
        {
            return now - _firstAttemptAt;
        }

        return TimeSpan.Zero;
    }

    public void RecordAttempt(DateTimeOffset now)
    {
        if (_firstAttemptAt == DateTimeOffset.MinValue)
        {
            _firstAttemptAt = now;
        }

        _attemptCount++;
    }

    public bool ShouldRequestRestart(DateTimeOffset now)
    {
        if (_attemptCount > _attemptLimit)
        {
            return true;
        }

        if (_attemptCount >= 3)
        {
            return Elapsed(now) >= _timeout;
        }

        return false;
    }

    public void Reset()
    {
        _firstAttemptAt = DateTimeOffset.MinValue;
        _attemptCount = 0;
    }
}