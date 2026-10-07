// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Strategy;
public interface INeteaseBoxWinRateSource
{
    void SetWinRateCaptureEnabled(bool enabled);
    IReadOnlyList<NeteaseBoxWinRateObservation> ReadCachedWinRates();
    Task RefreshWinRatesAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}