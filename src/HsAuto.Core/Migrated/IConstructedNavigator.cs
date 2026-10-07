// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;
using HsAuto.Core.Monitoring;

namespace HsAuto.Core.Automation;
public interface IConstructedNavigator
{
    string? BlockingStatus { get; }

    ClientConnectionState ConnectionState => ClientConnectionState.Unknown;

    long MatchmakingObservationSequence { get; }

    long MatchStartRequestSequence { get; }

    long PostGameTransitionProgressSequence { get; }

    MatchmakingActivityState MatchmakingActivityState { get; }

    event Action<string>? Log;
    event Action<ConstructedGameResult>? GameCompleted;
    Task<bool> TryPrepareConstructedAsync(ConstructedSettings settings, CancellationToken cancellationToken);
    Task<bool> TryHandleConfirmedGameOverAsync(ConstructedSettings settings, CancellationToken cancellationToken);
    Task<bool> TryRecoverDisconnectedClientOnlyAsync(CancellationToken cancellationToken);
}