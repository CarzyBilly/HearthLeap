// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Automation;
public sealed record MatchmakingMonitorResult(bool IsMatching, bool RecoveryTriggered, TimeSpan Elapsed, int PopupInstanceId, int CancelButtonInstanceId, bool CanCancel)
{
    public bool IsTransitioningToGame { get; init; }
    public bool RestartRequested { get; init; }
    public bool StateReadSucceeded { get; init; }
    public static MatchmakingMonitorResult NotMatching { get; } = new MatchmakingMonitorResult(IsMatching: false, RecoveryTriggered: false, TimeSpan.Zero, 0, 0, CanCancel: false)
    {
        StateReadSucceeded = true
    };
    public static MatchmakingMonitorResult Unavailable { get; } = new MatchmakingMonitorResult(IsMatching: false, RecoveryTriggered: false, TimeSpan.Zero, 0, 0, CanCancel: false);
    public static MatchmakingMonitorResult TransitioningToGame { get; } = NotMatching with
    {
        IsTransitioningToGame = true
    };
}