// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Automation;
public sealed record HearthstoneAccountDetailsSnapshot(bool Ready, bool LoggedIn, bool RewardTracksReady, bool GoldAvailable, bool InGame, string Error, long ElapsedMilliseconds, int TrackId, string TrackName, int Season, int CurrentLevel, long CurrentLevelExperience, long NextLevelExperience, long TotalExperience, long MaximumExperience, int LevelSoftCap, int LevelHardCap, long Gold, bool QuestsReady, IReadOnlyList<HearthstoneAccountQuestSnapshot> Quests)
{
    public string GoldError { get; init; } = "";
    public bool DustAvailable { get; init; }
    public long SpendableDust { get; init; }
    public long MassDisenchantDust { get; init; }
    public long TotalDust { get; init; }
    public bool RankedPlayAvailable { get; init; }
    public HearthstoneConstructedRankSnapshot StandardRank { get; init; } = HearthstoneConstructedRankSnapshot.Unavailable("Standard");
    public HearthstoneConstructedRankSnapshot WildRank { get; init; } = HearthstoneConstructedRankSnapshot.Unavailable("Wild");
    public bool ArenaTicketsAvailable { get; init; }
    public long ArenaTickets { get; init; }
    public long? CardPackCount { get; init; }
    public bool? UndergroundArenaUnlocked { get; init; }
    public bool BattlegroundsRatingAvailable { get; init; }
    public int BattlegroundsRating { get; init; }

    public static HearthstoneAccountDetailsSnapshot Unavailable(string error)
    {
        return new HearthstoneAccountDetailsSnapshot(Ready: false, LoggedIn: false, RewardTracksReady: false, GoldAvailable: false, InGame: false, error, 0L, 0, "", 0, 0, 0L, 0L, 0L, 0L, 0, 0, 0L, QuestsReady: false, Array.Empty<HearthstoneAccountQuestSnapshot>());
    }
}