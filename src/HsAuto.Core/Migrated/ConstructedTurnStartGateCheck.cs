// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Bot;
internal sealed record ConstructedTurnStartGateCheck(bool CanProceed, bool RopeBypass, string Reason)
{
    public static ConstructedTurnStartGateCheck NotApplicable { get; } = new ConstructedTurnStartGateCheck(CanProceed: true, RopeBypass: false, "");
    public static ConstructedTurnStartGateCheck Ready { get; } = new ConstructedTurnStartGateCheck(CanProceed: true, RopeBypass: false, "");
}