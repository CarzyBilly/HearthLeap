// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Models;
public sealed record ConstructedDecisionEnvelope
{
    public string MatchId { get; init; } = "local";
    public long ObservedStateSequence { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string StrategyName { get; init; } = "Unknown";
    public bool DryRun { get; init; }
    public IReadOnlyList<ConstructedAction> Actions { get; init; } = Array.Empty<ConstructedAction>();
    public string Summary { get; init; } = "";
}