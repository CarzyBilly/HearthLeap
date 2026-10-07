// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;

namespace HsAuto.Core.Configuration;
public static class AccountRunStopPolicy
{
    public const string DefaultConstructedRankTarget = "Diamond5";
    public const int DefaultLegendRankThreshold = 30000;
    public const int MinimumLegendRankThreshold = 1;
    public const int MaximumLegendRankThreshold = 1000000;
    public const int DefaultGoldThreshold = 1500;
    public const int MinimumGoldThreshold = 0;
    public const int MaximumGoldThreshold = 1000000;
    public static IReadOnlyList<ConstructedRankTarget> ConstructedRankTargets { get; } = new _003C_003Ez__ReadOnlyArray<ConstructedRankTarget>(new ConstructedRankTarget[10] { new ConstructedRankTarget("Legend", "传说分段", int.MaxValue), new ConstructedRankTarget("Diamond5", "钻石5", 46), new ConstructedRankTarget("Diamond10", "钻石10", 41), new ConstructedRankTarget("Platinum5", "白金5", 36), new ConstructedRankTarget("Platinum10", "白金10", 31), new ConstructedRankTarget("Gold10", "黄金10", 21), new ConstructedRankTarget("Gold5", "黄金5", 26), new ConstructedRankTarget("Silver10", "白银10", 11), new ConstructedRankTarget("Silver5", "白银5", 16), new ConstructedRankTarget("Bronze5", "青铜5", 6) });

    public static string NormalizeConstructedRankTarget(string? value)
    {
        string normalized = value?.Trim() ?? "";
        return ConstructedRankTargets.FirstOrDefault((ConstructedRankTarget target) => string.Equals(target.Key, normalized, StringComparison.OrdinalIgnoreCase) || string.Equals(target.DisplayName, normalized, StringComparison.OrdinalIgnoreCase))?.Key ?? "Diamond5";
    }

    public static ConstructedRankTarget GetConstructedRankTarget(string? value)
    {
        string key = NormalizeConstructedRankTarget(value);
        return ConstructedRankTargets.First((ConstructedRankTarget target) => string.Equals(target.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public static AccountRunStopDecision Evaluate(BotSettings settings, HearthstoneAccountDetailsSnapshot snapshot, ConstructedFormat? constructedFormat)
    {
        ArgumentNullException.ThrowIfNull(settings, "settings");
        ArgumentNullException.ThrowIfNull(snapshot, "snapshot");
        List<string> list = new List<string>();
        if (settings.EnableStopAtGold && snapshot.GoldAvailable && snapshot.Gold >= settings.StopAtGold)
        {
            list.Add($"金币 {snapshot.Gold} 已达到停止值 {settings.StopAtGold}");
        }

        HearthstoneConstructedRankSnapshot hearthstoneConstructedRankSnapshot = constructedFormat switch
        {
            ConstructedFormat.Standard => snapshot.StandardRank,
            ConstructedFormat.Wild => snapshot.WildRank,
            _ => HearthstoneConstructedRankSnapshot.Unavailable(""),
        };
        if (hearthstoneConstructedRankSnapshot.Available && !hearthstoneConstructedRankSnapshot.IsNewPlayer)
        {
            if (settings.EnableStopAtConstructedRank)
            {
                ConstructedRankTarget constructedRankTarget = GetConstructedRankTarget(settings.StopAtConstructedRank);
                if (hearthstoneConstructedRankSnapshot.IsLegend || hearthstoneConstructedRankSnapshot.StarLevel >= constructedRankTarget.MinimumStarLevel)
                {
                    list.Add("传统段位 " + hearthstoneConstructedRankSnapshot.DisplayName + " 已达到停止段位 " + constructedRankTarget.DisplayName);
                }
            }

            if (settings.EnableStopAtLegendRank && hearthstoneConstructedRankSnapshot.IsLegend && hearthstoneConstructedRankSnapshot.LegendRank > 0 && hearthstoneConstructedRankSnapshot.LegendRank <= settings.StopAtLegendRank)
            {
                list.Add($"传说排名 {hearthstoneConstructedRankSnapshot.LegendRank} 已进入停止线 {settings.StopAtLegendRank} 名以内");
            }
        }

        if (list.Count != 0)
        {
            return new AccountRunStopDecision(ShouldStop: true, string.Join("；", list));
        }

        return AccountRunStopDecision.Continue;
    }
}