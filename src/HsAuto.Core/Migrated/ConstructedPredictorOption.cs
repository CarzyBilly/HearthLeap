// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Models;
public sealed record ConstructedPredictorOption
{
    public int Id { get; init; }
    public int EntityId { get; init; }
    public int PlayerId { get; init; }
    public string Type { get; init; } = "";
    public string Error { get; init; } = "INVALID";
    public IReadOnlyList<int> TargetEntityIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<ConstructedPredictorSubOption> SubOptions { get; init; } = Array.Empty<ConstructedPredictorSubOption>();
}