// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Configuration;
public sealed record ConstructedRankControlDecision(bool ShouldConcede, ConstructedRankControlTarget EffectiveTarget, bool TargetAdjusted, string Reason);