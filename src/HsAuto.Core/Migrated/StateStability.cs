// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Automation;
internal sealed record StateStability(int PhaseStableMs, int HandStableMs, int BoardStableMs, int ActionStableMs, int ChoiceStableMs);