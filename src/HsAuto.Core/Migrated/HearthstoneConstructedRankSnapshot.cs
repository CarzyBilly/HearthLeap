// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Automation;
public sealed record HearthstoneConstructedRankSnapshot(bool Available, string Format, bool IsNewPlayer, bool IsLegend, int LegendRank, int StarLevel, int LeagueId, string RankName, string MedalText, string CheatName, string DisplayName, string Error)
{
    public int Stars { get; init; }

    public static HearthstoneConstructedRankSnapshot Unavailable(string format, string error = "")
    {
        return new HearthstoneConstructedRankSnapshot(Available: false, format, IsNewPlayer: false, IsLegend: false, 0, 0, 0, "", "", "", "", error);
    }
}