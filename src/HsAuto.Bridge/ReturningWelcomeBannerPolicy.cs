using System;

namespace HsAuto.UnityBridge;

internal static class ReturningWelcomeBannerPolicy
{
	public const string RootPath = "/OverlayUI(Clone)/UICanvasHeightScale/Center/WoodenSign_Paint_Welcome_Back(Clone)";

	public static bool IsCandidate(string componentType, string path)
	{
		if (string.Equals(componentType, "BannerPopup", StringComparison.Ordinal))
		{
			return string.Equals(path, "/OverlayUI(Clone)/UICanvasHeightScale/Center/WoodenSign_Paint_Welcome_Back(Clone)", StringComparison.Ordinal);
		}
		return false;
	}
}
