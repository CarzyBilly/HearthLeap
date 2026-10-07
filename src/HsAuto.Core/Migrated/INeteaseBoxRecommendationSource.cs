// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Strategy;
public interface INeteaseBoxRecommendationSource
{
    event Action<string>? Log;
    Task InitializeAsync(int hearthstoneProcessId, CancellationToken cancellationToken);
    void SetRecommendationEpoch(DateTimeOffset epoch)
    {
    }

    Task<IReadOnlyList<NeteaseBoxRecommendationObservation>> ReadAsync(int desiredOptionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NeteaseBoxRecommendationObservation>> RefreshAsync(int desiredOptionId, CancellationToken cancellationToken)
    {
        return ReadAsync(desiredOptionId, cancellationToken);
    }

    // Relocate readers only. Must not restart the box, change account binding, or
    // re-date historical recommendations as if they had just arrived.
    Task<bool> RelocateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    Task<bool> RestartAndReconnectAsync(int hearthstoneProcessId, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(result: false);
    }

    Task<bool> StopForClientRestartAsync(int hearthstoneProcessId, string reason, CancellationToken cancellationToken)
    {
        return Task.FromResult(result: false);
    }
}
