// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using HsAuto.Core.Automation;

namespace HsAuto.Core.Configuration;
public static class ConstructedRankControlPolicy
{
    public const string DefaultTarget = "Diamond5";
    public const int ConcedeAtStars = 2;
    public static IReadOnlyList<ConstructedRankControlTarget> Targets { get; } = new _003C_003Ez__ReadOnlyArray<ConstructedRankControlTarget>(new ConstructedRankControlTarget[11] { new ConstructedRankControlTarget("Legend", "传说分段", int.MaxValue, "Legend"), new ConstructedRankControlTarget("Diamond5", "钻石5", 46, "Diamond"), new ConstructedRankControlTarget("Diamond10", "钻石10", 41, "Diamond"), new ConstructedRankControlTarget("Platinum5", "白金5", 36, "Platinum"), new ConstructedRankControlTarget("Platinum10", "白金10", 31, "Platinum"), new ConstructedRankControlTarget("Gold5", "黄金5", 26, "Gold"), new ConstructedRankControlTarget("Gold10", "黄金10", 21, "Gold"), new ConstructedRankControlTarget("Silver5", "白银5", 16, "Silver"), new ConstructedRankControlTarget("Silver10", "白银10", 11, "Silver"), new ConstructedRankControlTarget("Bronze5", "青铜5", 6, "Bronze"), new ConstructedRankControlTarget("Bronze10", "青铜10", 1, "Bronze") });

    public static string NormalizeTarget(string? value)
    {
        string normalized = value?.Trim() ?? "";
        return Targets.FirstOrDefault((ConstructedRankControlTarget target) => string.Equals(target.Key, normalized, StringComparison.OrdinalIgnoreCase) || string.Equals(target.DisplayName, normalized, StringComparison.OrdinalIgnoreCase))?.Key ?? "Diamond5";
    }

    public static ConstructedRankControlTarget GetTarget(string? value)
    {
        string key = NormalizeTarget(value);
        return Targets.First((ConstructedRankControlTarget target) => string.Equals(target.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public static ConstructedRankControlDecision Evaluate(bool enabled, string? configuredTarget, HearthstoneConstructedRankSnapshot rank, bool continueDrainingToZero = false)
    {
        ArgumentNullException.ThrowIfNull(rank, "rank");
        ConstructedRankControlTarget target = GetTarget(configuredTarget);
        if (!enabled || !rank.Available || rank.IsNewPlayer)
        {
            return new ConstructedRankControlDecision(ShouldConcede: false, target, TargetAdjusted: false, "");
        }

        ConstructedRankControlTarget protectedTarget = GetProtectedTarget(target, rank);
        bool targetAdjusted = !string.Equals(protectedTarget.Key, target.Key, StringComparison.Ordinal);
        if (rank.IsLegend || string.Equals(protectedTarget.Key, "Legend", StringComparison.Ordinal))
        {
            return new ConstructedRankControlDecision(ShouldConcede: false, protectedTarget, targetAdjusted, "已进入传说，普通分段控星不再投降");
        }

        bool flag = rank.StarLevel > protectedTarget.MinimumStarLevel;
        bool flag2 = rank.StarLevel == protectedTarget.MinimumStarLevel;
        bool flag3 = flag2 && rank.Stars >= 2;
        bool flag4 = (continueDrainingToZero & flag2) && rank.Stars > 0;
        bool num = flag | flag3 | flag4;
        string reason;
        if (num)
        {
            reason = ((flag4 && !flag3) ? $"{protectedTarget.DisplayName} 当前 {rank.Stars} 星，继续投降至 0 星" : $"{protectedTarget.DisplayName} 当前 {rank.Stars} 星，达到 {2} 星控分线");
        }
        else
        {
            reason = $"{protectedTarget.DisplayName} 当前 {rank.Stars} 星，未达到 {2} 星控分线";
        }

        return new ConstructedRankControlDecision(num, protectedTarget, targetAdjusted, reason);
    }

    private static ConstructedRankControlTarget GetProtectedTarget(ConstructedRankControlTarget configured, HearthstoneConstructedRankSnapshot rank)
    {
        if (rank.IsLegend)
        {
            return Targets.First((ConstructedRankControlTarget target) => target.Key == "Legend");
        }

        ConstructedRankControlTarget constructedRankControlTarget = (
            from target in Targets
            where target.MinimumStarLevel != int.MaxValue
            where target.MinimumStarLevel <= rank.StarLevel
            orderby target.MinimumStarLevel descending
            select target).FirstOrDefault();
        if ((object)constructedRankControlTarget == null || configured.MinimumStarLevel >= constructedRankControlTarget.MinimumStarLevel)
        {
            return configured;
        }

        return constructedRankControlTarget;
    }
}