using System;

namespace HsAuto.UnityBridge;

internal static class RewardPopupObjectPolicy
{
	internal static bool IsLikelyBlocking(string name, string path, bool active, bool hiddenBoosterPackReward = false)
	{
		if (!active | hiddenBoosterPackReward)
		{
			return false;
		}
		if (string.Equals(name, "BG27_Anomaly_Prizes2(Clone)", StringComparison.OrdinalIgnoreCase) || string.Equals(path, "/BG27_Anomaly_Prizes2(Clone)", StringComparison.OrdinalIgnoreCase) || (path ?? "").EndsWith("/BG27_Anomaly_Prizes2(Clone)", StringComparison.OrdinalIgnoreCase) || (path ?? "").IndexOf("/BG27_Anomaly_Prizes2(Clone)/", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		string value = name + " " + path;
		if (ContainsAny(value, "RewardScroll", "BoosterPackReward", "RewardToast", "LootToast", "RewardTrack", "RankedReward", "PostGameReward", "RewardScreen", "LevelUpReward", "GoldReward", "EndOfGameMultiQuestRewardFlow", "QuestXpReward", "Prize"))
		{
			return true;
		}
		if (ContainsAny(value, "RewardChest") && !ContainsAny(value, "RewardChestBone"))
		{
			return ContainsAny(value, "Popup", "Scroll", "Toast", "RewardTrack");
		}
		return false;
	}

	private static bool ContainsAny(string value, params string[] candidates)
	{
		foreach (string value2 in candidates)
		{
			if (value.IndexOf(value2, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}
}
