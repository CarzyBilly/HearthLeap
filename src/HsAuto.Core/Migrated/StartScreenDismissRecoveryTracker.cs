// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Automation;
public sealed class StartScreenDismissRecoveryTracker
{
    public const int DefaultAttemptLimit = 10;
    private readonly int _attemptLimit;
    private int _successfulAttemptCount;
    public int SuccessfulAttemptCount => _successfulAttemptCount;

    public StartScreenDismissRecoveryTracker(int attemptLimit = 10)
    {
        if (attemptLimit < 1)
        {
            throw new ArgumentOutOfRangeException("attemptLimit");
        }

        _attemptLimit = attemptLimit;
    }

    public bool RecordSuccessfulAttempt()
    {
        _successfulAttemptCount++;
        return _successfulAttemptCount > _attemptLimit;
    }

    public void Reset()
    {
        _successfulAttemptCount = 0;
    }
}