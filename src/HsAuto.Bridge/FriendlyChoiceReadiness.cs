using System.Collections.Generic;
using System.Linq;

namespace HsAuto.UnityBridge;

internal static class FriendlyChoiceReadiness
{
	internal static bool IsDarkGift(string sourceCardId, bool candidateHasDarkGift)
	{
		if (!candidateHasDarkGift && !(sourceCardId == "FIR_939") && !(sourceCardId == "EDR_102t"))
		{
			return sourceCardId == "BG36_Button_DarkGift";
		}
		return true;
	}

	internal static bool CanSelect(int packetId, int displayId, bool waiting, bool revealed, bool concealed, IReadOnlyCollection<int> expectedIds, IReadOnlyCollection<int> readyVisibleIds)
	{
		if (((packetId > 0 && packetId == displayId && !waiting) & revealed) && !concealed && expectedIds.Count > 0 && expectedIds.Count == readyVisibleIds.Count && expectedIds.Distinct().Count() == expectedIds.Count && readyVisibleIds.Distinct().Count() == readyVisibleIds.Count)
		{
			return expectedIds.All(((IEnumerable<int>)readyVisibleIds).Contains<int>);
		}
		return false;
	}
}
