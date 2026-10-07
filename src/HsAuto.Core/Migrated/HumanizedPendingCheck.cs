// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using HsAuto.Core.Models;

namespace HsAuto.Core.Bot;
internal sealed record HumanizedPendingCheck(HumanizedPendingStatus Status, ConstructedDecisionEnvelope? Decision, int DelayMs, TimeSpan Remaining, bool RopeBypass, string Reason)
{
    public static HumanizedPendingCheck None { get; } = new HumanizedPendingCheck(HumanizedPendingStatus.None, null, 0, TimeSpan.Zero, RopeBypass: false, "");

    public static HumanizedPendingCheck Waiting(ConstructedDecisionEnvelope decision, int delayMs, TimeSpan remaining)
    {
        return new HumanizedPendingCheck(HumanizedPendingStatus.Waiting, decision, delayMs, remaining, RopeBypass: false, "");
    }

    public static HumanizedPendingCheck Ready(ConstructedDecisionEnvelope decision, int delayMs, bool ropeBypass)
    {
        return new HumanizedPendingCheck(HumanizedPendingStatus.Ready, decision, delayMs, TimeSpan.Zero, ropeBypass, "");
    }

    public static HumanizedPendingCheck Canceled(string reason)
    {
        return new HumanizedPendingCheck(HumanizedPendingStatus.Canceled, null, 0, TimeSpan.Zero, RopeBypass: false, reason);
    }
}