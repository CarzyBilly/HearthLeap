// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
internal sealed class HumanizedInteractionPacer
{
    private readonly HumanizedDelayModel _delayModel;
    private readonly Func<int, CancellationToken, Task> _delayAsync;
    private long _sequence;
    public HumanizedInteractionPacer(HumanizedDelayModel? delayModel = null, Func<int, CancellationToken, Task>? delayAsync = null)
    {
        _delayModel = delayModel ?? new HumanizedDelayModel();
        _delayAsync = delayAsync ?? ((Func<int, CancellationToken, Task>)((int milliseconds, CancellationToken token) => Task.Delay(milliseconds, token)));
    }

    public async Task<int> DelayAsync(HumanizedActionCategory category, bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled)
        {
            return 0;
        }

        (int, int) tuple = category switch
        {
            HumanizedActionCategory.MatchStart => (1500, 5000),
            HumanizedActionCategory.PostGameQueue => (4000, 12000),
            HumanizedActionCategory.AccountMutation => (800, 2800),
            HumanizedActionCategory.Choice => (900, 3200),
            _ => (500, 1800),
        };
        int item = tuple.Item1;
        int item2 = tuple.Item2;
        long num = Interlocked.Increment(ref _sequence);
        int delayMs = _delayModel.NextDelayMs(category, $"interaction-{num / 5}", item, item2);
        await _delayAsync(delayMs, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        return delayMs;
    }
}