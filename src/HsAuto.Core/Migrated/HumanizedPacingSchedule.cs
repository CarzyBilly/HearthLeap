// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using HsAuto.Core.Models;

namespace HsAuto.Core.Bot;
internal sealed record HumanizedPacingSchedule(ConstructedAction Action, int DelayMs);