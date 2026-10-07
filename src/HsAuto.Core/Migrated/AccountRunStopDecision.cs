// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Configuration;
public sealed record AccountRunStopDecision(bool ShouldStop, string Reason)
{
    public static AccountRunStopDecision Continue { get; } = new AccountRunStopDecision(ShouldStop: false, "");
}