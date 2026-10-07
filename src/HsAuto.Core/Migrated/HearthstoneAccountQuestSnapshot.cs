// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Automation;
public sealed record HearthstoneAccountQuestSnapshot(int QuestId, string PoolType, string Name, string Description, int Progress, int Quota, int RewardTrackExperience, string Status, bool IsChainQuest);