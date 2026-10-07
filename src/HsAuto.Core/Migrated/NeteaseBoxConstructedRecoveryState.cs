// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Threading;

namespace HsAuto.Core.Strategy;
public sealed class NeteaseBoxConstructedRecoveryState
{
    private int _awaitingRecommendationAfterClientRestart;
    private int _recoveryAttemptCount;
    public bool AwaitingRecommendationAfterClientRestart => Volatile.Read(in _awaitingRecommendationAfterClientRestart) != 0;
    public int RecoveryAttemptCount => Volatile.Read(in _recoveryAttemptCount);

    public int BeginRecoveryAttempt()
    {
        return Interlocked.Increment(ref _recoveryAttemptCount);
    }

    public void MarkClientRestartRequested()
    {
        Interlocked.Exchange(ref _awaitingRecommendationAfterClientRestart, 1);
    }

    public void MarkHealthy()
    {
        Interlocked.Exchange(ref _awaitingRecommendationAfterClientRestart, 0);
        Interlocked.Exchange(ref _recoveryAttemptCount, 0);
    }
}