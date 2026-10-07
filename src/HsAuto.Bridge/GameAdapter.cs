using System; using System.Collections; using System.Collections.Concurrent; using System.Collections.Generic;
using System.Diagnostics; using System.Globalization; using System.IO; using System.IO.Pipes; using System.Linq;
using System.Reflection; using System.Runtime.CompilerServices; using System.Text; using System.Text.RegularExpressions;
using System.Threading; using BepInEx; using BepInEx.Logging; using UnityEngine; using UnityEngine.Events;
using UnityEngine.SceneManagement; using UnityEngine.UI; using Object = UnityEngine.Object;
namespace HsAuto.UnityBridge {
public sealed partial class OpenBridgePlugin : BaseUnityPlugin {	private sealed class ConstructedDeckAvailability
	{
		public bool? IsPlayable { get; set; }

		public int? CardCount { get; set; }

		public int? MinimumCardCount { get; set; }

		public int? MaximumCardCount { get; set; }

		public int? UsableCardCount { get; set; }

		public bool CanAutoComplete { get; set; }

		public string Reason { get; set; } = "卡组数据尚未就绪";
	}


	private sealed class DeckCompletion
	{
		public object Deck;

		public object Tray;

		public DateTime StartedAt;

		public bool CallbackFinished;

		public string Error = "";
	}


	private sealed class MainMenuReadiness
	{
		public bool activeGameplay { get; set; }

		public bool atMainMenu { get; set; }

		public bool startScreen { get; set; }

		public bool startupPending { get; set; }

		public int startupPopupId { get; set; }

		public string startupPopupKind { get; set; } = "";

		public bool ready { get; set; }

		public bool rewardPending { get; set; }

		public string scene { get; set; } = "";

		public string boxState { get; set; } = "";

		public bool isReturningPlayer { get; set; }

		public string returningPlayerStatus { get; set; } = "";

		public bool returningStartupPending { get; set; }

		public int returningWelcomeBannerId { get; set; }

		public string returningStage { get; set; } = "";
	}


	private sealed class CachedMethod
	{
		public MethodInfo Method { get; }

		public CachedMethod(MethodInfo method)
		{
			Method = method;
		}
	}


	private sealed class CachedProperty
	{
		public static readonly CachedProperty Missing = new CachedProperty(null);

		public PropertyInfo Property { get; }

		public CachedProperty(PropertyInfo property)
		{
			Property = property;
		}
	}


	private sealed class CachedField
	{
		public FieldInfo Field { get; }

		public CachedField(FieldInfo field)
		{
			Field = field;
		}
	}


	private sealed class QuestStatusItem
	{
		public int QuestId { get; set; }

		public int PoolId { get; set; }

		public string PoolType { get; set; } = "";

		public string Name { get; set; } = "";

		public string Description { get; set; } = "";

		public int Progress { get; set; }

		public int Quota { get; set; }

		public int RerollCount { get; set; }

		public int RewardTrackXp { get; set; }

		public string Status { get; set; } = "";

		public bool IsChainQuest { get; set; }

		public string TimeUntilExpiration { get; set; } = "";
	}


	private sealed class QuestPoolStatusItem
	{
		public int PoolId { get; set; }

		public string PoolType { get; set; } = "";

		public int RerollAvailableCount { get; set; }

		public int SecondsUntilNextGrant { get; set; }

		public int BankedQuestCount { get; set; }
	}


	private sealed class Selector
	{
		public int? InstanceId;

		public string Path;

		public string PathContains;

		public string Name;

		public string NameContains;

		public string Tag;

		public string Component;

		public string TextContains;

		public bool HasExactIdentitySelector
		{
			get
			{
				if (!InstanceId.HasValue)
				{
					return !string.IsNullOrWhiteSpace(Path);
				}
				return true;
			}
		}

		public bool HasComponentSelector => !string.IsNullOrWhiteSpace(Component);

		public static Selector From(string json)
		{
			if (json == null)
			{
				json = "";
			}
			return new Selector
			{
				InstanceId = GetNullableInt(json, "instanceId"),
				Path = GetString(json, "path"),
				PathContains = GetString(json, "pathContains"),
				Name = GetString(json, "name"),
				NameContains = GetString(json, "nameContains"),
				Tag = GetString(json, "tag"),
				Component = GetString(json, "component"),
				TextContains = GetString(json, "textContains")
			};
		}

		public bool Matches(GameObject gameObject, bool componentAlreadyMatched = false)
		{
			if (InstanceId.HasValue && ((Object)gameObject).GetInstanceID() != InstanceId.Value)
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(Path) && !string.Equals(GetPath(gameObject), Path, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(PathContains) && GetPath(gameObject).IndexOf(PathContains, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(Name) && !string.Equals(((Object)gameObject).name, Name, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(NameContains) && ((Object)gameObject).name.IndexOf(NameContains, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(Tag) && !string.Equals(SafeGetTag(gameObject), Tag, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!componentAlreadyMatched && !string.IsNullOrWhiteSpace(Component) && !gameObject.GetComponents<Component>().Any((Component component) => (Object)(object)component != (Object)null && ComponentTypeMatches(component, Component)))
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(TextContains) && GetObjectText(gameObject).IndexOf(TextContains, StringComparison.OrdinalIgnoreCase) < 0)
			{
				return false;
			}
			return true;
		}
	}


	private sealed class ConstructedRankReadResult
	{
		public bool Available { get; }

		public string Format { get; }

		public bool IsNewPlayer { get; }

		public bool IsLegend { get; }

		public int LegendRank { get; }

		public int StarLevel { get; }

		public int Stars { get; }

		public int LeagueId { get; }

		public string RankName { get; }

		public string MedalText { get; }

		public string CheatName { get; }

		public string DisplayName { get; }

		public string Reason { get; }

		public ConstructedRankReadResult(bool available, string format, bool isNewPlayer, bool isLegend, int legendRank, int starLevel, int stars, int leagueId, string rankName, string medalText, string cheatName, string displayName, string reason)
		{
			Available = available;
			Format = format;
			IsNewPlayer = isNewPlayer;
			IsLegend = isLegend;
			LegendRank = legendRank;
			StarLevel = starLevel;
			Stars = stars;
			LeagueId = leagueId;
			RankName = rankName;
			MedalText = medalText;
			CheatName = cheatName;
			DisplayName = displayName;
			Reason = reason;
		}

		public static ConstructedRankReadResult Unavailable(string format, string reason)
		{
			return new ConstructedRankReadResult(available: false, format, isNewPlayer: false, isLegend: false, 0, 0, 0, 0, "", "", "", "", reason);
		}
	}


	private sealed class RewardPopupDismissResult
	{
		public string Kind { get; set; }

		public bool Visible { get; set; }

		public bool Dismissed { get; set; }

		public object Target { get; set; }

		public string Method { get; set; }

		public string Error { get; set; }
	}


	private sealed class SetRotationTransitionResult
	{
		public bool Visible { get; set; }

		public bool Dismissed { get; set; }

		public bool Returned { get; set; }

		public bool Pending { get; set; }

		public bool Failed { get; set; }

		public string Stage { get; set; } = "";

		public string BoxState { get; set; } = "";

		public string TutorialState { get; set; } = "";

		public object Target { get; set; }

		public string Method { get; set; } = "";

		public string Error { get; set; } = "";
	}


	private sealed class MatchmakingUiState
	{
		public GameObject Popup { get; set; }

		public GameObject CancelButton { get; set; }

		public GameObject[] CancelCandidates { get; set; } = Array.Empty<GameObject>();
	}


	private sealed class ConstructedDeckCandidate
	{
		public GameObject GameObject { get; set; }

		public Component Visual { get; set; }

		public int Index { get; set; }

		public int GlobalIndex { get; set; }

		public int PageIndex { get; set; }

		public int PageOrdinal { get; set; }

		public string Name { get; set; }

		public string NormalizedName { get; set; }

		public string Path { get; set; }

		public int InstanceId { get; set; }

		public bool Active { get; set; }

		public bool Selected { get; set; }
	}


	private sealed class ConstructedDeckScanResult
	{
		public bool LeftClaimedLoanerPage { get; set; }

		public List<ConstructedDeckCandidate> Decks { get; } = new List<ConstructedDeckCandidate>();

		public List<int> VisitedPageIndexes { get; } = new List<int>();

		public ConstructedDeckCandidate Target { get; set; }

		public int OriginalPageIndex { get; set; } = -1;

		public int FinalPageIndex { get; set; } = -1;

		public bool ReachedLastPage { get; set; }

		public bool UsedInternalPaging { get; set; }

		public bool RestoredOriginalPage { get; set; }
	}


	private sealed class OwnedConstructedDeckCandidate
	{
		public long Id { get; set; }

		public object Deck { get; set; }

		public object OriginalFormat { get; set; }

		public string Name { get; set; } = "";

		public string FormatType { get; set; } = "";

		public string FormatSource { get; set; } = "";

		public bool IsStandard { get; set; }

		public bool IsWild { get; set; }

		public ConstructedDeckAvailability Availability { get; set; }
	}


	private sealed class ConstructedSubOptionCandidate
	{
		public int Index { get; set; }

		public object Part { get; set; }

		public int EntityId { get; set; }

		public string CardId { get; set; }

		public int ZonePosition { get; set; }

		public bool IsValid { get; set; }
	}


	private sealed class BridgeResponse
	{
		public bool Ok { get; set; }

		public string Error { get; set; }

		public object Data { get; set; }

		public static BridgeResponse Success(object data)
		{
			return new BridgeResponse
			{
				Ok = true,
				Data = data
			};
		}

		public static BridgeResponse Failure(string error)
		{
			return new BridgeResponse
			{
				Ok = false,
				Error = error
			};
		}

		public static BridgeResponse Failure(string error, object data)
		{
			return new BridgeResponse
			{
				Ok = false,
				Error = error,
				Data = data
			};
		}

		public string ToJson()
		{
			return LiteJson.Serialize(new Dictionary<string, object>
			{
				["Ok"] = Ok,
				["Error"] = Error,
				["Data"] = Data
			});
		}
	}


	private static class LiteJson
	{
		public static string Serialize(object value)
		{
			StringBuilder stringBuilder = new StringBuilder();
			Write(stringBuilder, value);
			return stringBuilder.ToString();
		}

		private static void Write(StringBuilder builder, object value)
		{
			if (value == null)
			{
				builder.Append("null");
				return;
			}
			if (value is string value2)
			{
				WriteString(builder, value2);
				return;
			}
			if (value is bool flag)
			{
				builder.Append(flag ? "true" : "false");
				return;
			}
			if ((value is int || value is long || value is float || value is double || value is decimal) ? true : false)
			{
				builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
				return;
			}
			if (value is IDictionary dictionary)
			{
				builder.Append('{');
				bool flag2 = true;
				foreach (DictionaryEntry item in dictionary)
				{
					if (!flag2)
					{
						builder.Append(',');
					}
					flag2 = false;
					WriteString(builder, Convert.ToString(item.Key, CultureInfo.InvariantCulture));
					builder.Append(':');
					Write(builder, item.Value);
				}
				builder.Append('}');
				return;
			}
			if (value is IEnumerable enumerable)
			{
				builder.Append('[');
				bool flag3 = true;
				foreach (object item2 in enumerable)
				{
					if (!flag3)
					{
						builder.Append(',');
					}
					flag3 = false;
					Write(builder, item2);
				}
				builder.Append(']');
				return;
			}
			builder.Append('{');
			PropertyInfo[] array = (from property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
				where property.GetIndexParameters().Length == 0
				select property).ToArray();
			for (int num = 0; num < array.Length; num++)
			{
				if (num > 0)
				{
					builder.Append(',');
				}
				WriteString(builder, array[num].Name);
				builder.Append(':');
				Write(builder, array[num].GetValue(value, null));
			}
			builder.Append('}');
		}

		private static void WriteString(StringBuilder builder, string value)
		{
			builder.Append('"');
			string text = value ?? "";
			foreach (char c in text)
			{
				switch (c)
				{
				case '\\':
					builder.Append("\\\\");
					break;
				case '"':
					builder.Append("\\\"");
					break;
				case '\n':
					builder.Append("\\n");
					break;
				case '\r':
					builder.Append("\\r");
					break;
				case '\t':
					builder.Append("\\t");
					break;
				default:
					builder.Append(c);
					break;
				}
			}
			builder.Append('"');
		}
	}


	private object _lastAcceptedCardReplacement;


	private readonly BattlegroundsTutorialIdentity _battlegroundsTutorialIdentity = new BattlegroundsTutorialIdentity();


	private readonly Dictionary<long, DeckCompletion> _deckCompletions = new Dictionary<long, DeckCompletion>();


	private int _closedReturningWelcomeBannerId;


	private Component _closingRotatedBoostersPopup;


	private GameObject[] _lastDarkGiftUnselectedObjects = Array.Empty<GameObject>();


	private int[] _lastDarkGiftUnselectedIds = Array.Empty<int>();


	private int _lastDarkGiftChoiceId;


	private int _lastDarkGiftSelectedId;


	private int _lastDarkGiftReadyCount;


	private string _lastDarkGiftSourceCardId = "";


	private static readonly ConcurrentDictionary<string, Type> LoadedTypeCache = new ConcurrentDictionary<string, Type>();


	private static readonly ConcurrentDictionary<string, CachedMethod> ZeroArgMethodCache = new ConcurrentDictionary<string, CachedMethod>();


	private static readonly ConcurrentDictionary<string, CachedMethod> EntityTagMethodCache = new ConcurrentDictionary<string, CachedMethod>();


	private static readonly ConcurrentDictionary<string, int> GameTagValueCache = new ConcurrentDictionary<string, int>();


	private static readonly ConcurrentDictionary<string, string> BattlegroundsCardTextCache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);


	private static readonly ConcurrentDictionary<string, CachedProperty> InstancePropertyCache = new ConcurrentDictionary<string, CachedProperty>();


	private static readonly ConcurrentDictionary<string, CachedField> InstanceFieldCache = new ConcurrentDictionary<string, CachedField>();


	private static readonly ConcurrentDictionary<long, long> RewardTrackMaximumExperienceCache = new ConcurrentDictionary<long, long>();


	private static readonly MethodInfo FindObjectFromInstanceIdMethod = typeof(Object).GetMethod("FindObjectFromInstanceID", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(int) }, null);


	private static readonly object GameTagTypeLock = new object();


	private static readonly string[] TextMemberNames = new string[6] { "text", "Text", "m_text", "m_Text", "label", "Label" };


	private static bool _gameTagTypeResolved;


	private static Type _gameTagType;


	private int _matchmakingPopupInstanceId;


	private DateTimeOffset _matchmakingObservedSinceUtc = DateTimeOffset.MinValue;


	private Component _cachedVisibleBattlegroundsEndGameScreen;


	private float _nextBattlegroundsEndGameProbeAt;


	private bool _setRotationReturnInFlight;


	private int _rankedRewardDismissedInstanceId;


	private static readonly CurrencyBalanceReader AccountCurrencyReader = new CurrencyBalanceReader();


	private static long? ReadCardPackCount()
	{
		if (!IsNetworkLoggedIn())
		{
			return null;
		}
		try
		{
			object obj = InvokeStaticNoArg("NetCache", "Get");
			Type type = FindLoadedType("NetCache+NetCacheBoosters");
			MethodInfo methodInfo = obj?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo method) => method.Name == "GetNetObject" && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0);
			return (InvokeNoArg((type == null) ? null : methodInfo?.MakeGenericMethod(type).Invoke(obj, null), "GetTotalNumBoosters") is int num && num >= 0) ? new long?(num) : ((long?)null);
		}
		catch
		{
			return null;
		}
	}


	private static object GetCardReplacementManager()
	{
		return ReadMember(InvokeStaticNoArg("PopupDisplayManager", "Get"), "m_redundantNDERerollPopups");
	}


	private static Component FindCardReplacementPopup()
	{
		object obj = ReadMember(GetCardReplacementManager(), "m_currentPopupComponent");
		Component val = (Component)((obj is Component) ? obj : null);
		if (!((Object)(object)val != (Object)null) || !((Object)(object)val.gameObject != (Object)null) || !val.gameObject.activeInHierarchy || !(((object)val).GetType().Name == "RedundantNDEPopup"))
		{
			return null;
		}
		return val;
	}


	private RewardPopupDismissResult TryAcceptCardReplacementPopup()
	{
		RewardPopupDismissResult rewardPopupDismissResult = new RewardPopupDismissResult
		{
			Kind = "RedundantNDEPopup"
		};
		if (IsActiveGameInProgress())
		{
			return rewardPopupDismissResult;
		}
		Component val = FindCardReplacementPopup();
		if ((Object)(object)val == (Object)null)
		{
			return rewardPopupDismissResult;
		}
		rewardPopupDismissResult.Visible = true;
		if (_lastAcceptedCardReplacement == val)
		{
			return rewardPopupDismissResult;
		}
		if (!(ReadMember(val, "RerollSelected") is Delegate))
		{
			return rewardPopupDismissResult;
		}
		MethodInfo methodInfo = ((object)val).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SingleOrDefault((MethodInfo method) => method.Name == "OnRerollSelected" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType.Name == "UIEvent");
		if (methodInfo == null)
		{
			return rewardPopupDismissResult;
		}
		try
		{
			methodInfo.Invoke(val, new object[1]);
			_lastAcceptedCardReplacement = val;
			rewardPopupDismissResult.Dismissed = true;
			rewardPopupDismissResult.Method = "RedundantNDEPopup.OnRerollSelected(UIEvent)";
			rewardPopupDismissResult.Target = Describe(val.gameObject, includeComponents: false);
			Logger.LogInfo("Selected card replacement via RedundantNDEPopup.OnRerollSelected.");
		}
		catch (Exception ex)
		{
			rewardPopupDismissResult.Error = ex.InnerException?.Message ?? ex.Message;
		}
		return rewardPopupDismissResult;
	}


	private static bool? ReadUndergroundArenaUnlocked()
	{
		if (!IsNetworkLoggedIn())
		{
			return null;
		}
		object obj = InvokeStaticNoArg("GameSaveDataManager", "Get");
		MethodInfo methodInfo = obj?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "IsDataReady" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
		if (methodInfo == null)
		{
			return null;
		}
		try
		{
			object obj2 = methodInfo.Invoke(obj, new object[1] { Enum.Parse(methodInfo.GetParameters()[0].ParameterType, "PLAYER_FLAGS") });
			if (!(obj2 is bool) || !(bool)obj2)
			{
				return null;
			}
		}
		catch
		{
			return null;
		}
		MethodInfo methodInfo2 = FindLoadedType("GameModeUtils")?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "HasUnlockedMode" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsEnum);
		if (methodInfo2 == null)
		{
			return null;
		}
		try
		{
			return methodInfo2.Invoke(null, new object[1] { Enum.Parse(methodInfo2.GetParameters()[0].ParameterType, "UNDERGROUND_ARENA") }) as bool?;
		}
		catch
		{
			return null;
		}
	}


	private static bool IsNativeBattlegroundsTutorial()
	{
		object instance = InvokeStaticNoArg("GameMgr", "Get");
		if (TryInvokeBool(instance, "IsBattlegroundsTutorial") && InvokeNoArg(instance, "GetGameType")?.ToString() == "GT_VS_AI")
		{
			return ReadIntMember(instance, "GetMissionId", 0) == 3539;
		}
		return false;
	}


	private int ReadBattlegroundsTutorialId(object gameEntity, int friendlyPlayerId)
	{
		return _battlegroundsTutorialIdentity.Observe((friendlyPlayerId > 0) ? gameEntity : null, IsNativeBattlegroundsTutorial);
	}


	private static bool HasClaimedLoanerDeck()
	{
		object instance = InvokeStaticNoArg("FreeDeckMgr", "Get");
		if (ReadIntValue(InvokeNoArg(instance, "get_Status"), 0) != 2)
		{
			return ReadIntValue(InvokeNoArg(instance, "get_ClaimedDeckTemplateId"), 0) > 0;
		}
		return true;
	}


	private static string CurrentConstructedDeckPageType(object tray)
	{
		return InvokeNoArg(InvokeNoArg(tray, "GetCurrentCustomPage"), "get_DeckPageType")?.ToString() ?? "";
	}


	private static bool LeaveClaimedLoanerDeckPage(object tray)
	{
		if (tray == null || !HasClaimedLoanerDeck())
		{
			return false;
		}
		object instance = InvokeNoArg(InvokeStaticNoArg("LoanerDeckDisplay", "Get"), "get_LoanerDeckInfoDataModel");
		if (CurrentConstructedDeckPageType(tray) != "LOANER_DECK_DISPLAY")
		{
			object obj = InvokeNoArg(instance, "get_IsCurrentPageLoaner");
			if (!(obj is bool) || !(bool)obj)
			{
				return false;
			}
		}
		if (!TryInvokeZeroArgMethod(tray, "BackOutToHub", out var _, out var error))
		{
			throw new InvalidOperationException(error);
		}
		return true;
	}


	private static ConstructedDeckAvailability ReadConstructedDeckAvailability(object deck, object originalFormat = null)
	{
		ConstructedDeckAvailability constructedDeckAvailability = new ConstructedDeckAvailability();
		try
		{
			if (deck == null)
			{
				goto IL_0028;
			}
			object obj = InvokeNoArg(deck, "NetworkContentsLoaded");
			if (!(obj is bool) || !(bool)obj)
			{
				goto IL_0028;
			}
			obj = InvokeNoArg(deck, "IsSavingChanges");
			if (obj is bool && (bool)obj)
			{
				constructedDeckAvailability.Reason = "等待游戏确认卡组保存";
				return constructedDeckAvailability;
			}
			object obj2 = originalFormat ?? InvokeNoArg(deck, "get_FormatType");
			object obj3 = deck.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "CountCardsByStatus" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(bool))?.Invoke(deck, new object[2] { obj2, false });
			if (obj3 == null || !(InvokeNoArg(deck, "get_IsValidForRuleset") is bool flag))
			{
				constructedDeckAvailability.Reason = "尚未读取到游戏卡组规则";
				return constructedDeckAvailability;
			}
			int num = ReadMemberAsInt(obj3, "Total", -1);
			int num2 = ReadMemberAsInt(obj3, "Min", -1);
			int num3 = ReadMemberAsInt(obj3, "Max", -1);
			if (num < 0 || num2 <= 0 || num3 < num2)
			{
				return constructedDeckAvailability;
			}
			constructedDeckAvailability.CardCount = num;
			constructedDeckAvailability.MinimumCardCount = num2;
			constructedDeckAvailability.MaximumCardCount = num3;
			int num4 = ReadMemberAsInt(obj3, "Invalid", -1);
			int num5 = ReadMemberAsInt(obj3, "Missing", -1);
			object obj4 = InvokeNoArg(deck, "GetMissingSideboardCardCount");
			object obj5 = deck.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "GetInvalidSideboardCardCount" && m.GetParameters().Length == 1)?.Invoke(deck, new object[1] { obj2 });
			if (num4 < 0 || num5 < 0 || !(obj4 is int num6) || !(obj5 is int num7))
			{
				return constructedDeckAvailability;
			}
			constructedDeckAvailability.UsableCardCount = Math.Max(0, num - num4 - num5);
			constructedDeckAvailability.CanAutoComplete = num <= num3 && (num < num2 || num4 > 0 || num5 > 0 || num6 > 0 || num7 > 0);
			constructedDeckAvailability.IsPlayable = flag && num >= num2 && num <= num3 && num4 == 0 && num5 == 0 && num6 == 0 && num7 == 0;
			ConstructedDeckAvailability constructedDeckAvailability2 = constructedDeckAvailability;
			string reason;
			if (num < num2)
			{
				reason = $"张数不足（{num}/{num2}）";
			}
			else if (num > num3)
			{
				reason = $"张数超出限制（{num}/{num3}）";
			}
			else if (num4 > 0 || num5 > 0)
			{
				reason = "含缺失或当前格式不可用的卡牌";
			}
			else if (num6 > 0 || num7 > 0)
			{
				reason = "备牌不完整或不可用";
			}
			else
			{
				reason = ((!flag) ? "不符合游戏卡组规则" : "");
			}
			constructedDeckAvailability2.Reason = reason;
			goto end_IL_0006;
			IL_0028:
			return constructedDeckAvailability;
			end_IL_0006:;
		}
		catch (Exception ex)
		{
			constructedDeckAvailability.Reason = "卡组规则读取失败：" + ex.GetBaseException().Message;
		}
		return constructedDeckAvailability;
	}


	private MainMenuReadiness ReadMainMenuReadiness()
	{
		MainMenuReadiness mainMenuReadiness = new MainMenuReadiness
		{
			activeGameplay = IsActiveGameInProgress()
		};
		if (mainMenuReadiness.activeGameplay)
		{
			return mainMenuReadiness;
		}
		object instance = InvokeStaticNoArg("SceneMgr", "Get");
		mainMenuReadiness.scene = TryInvokeNoArgValue(instance, "GetMode")?.ToString() ?? "";
		object obj = InvokeStaticNoArg("Box", "Get");
		Component component = (Component)((obj is Component) ? obj : null);
		mainMenuReadiness.boxState = ReadComponentState(component);
		if (string.IsNullOrWhiteSpace(mainMenuReadiness.boxState))
		{
			mainMenuReadiness.boxState = ReadBoxState(GameObject.Find("/TheBox(Clone)"));
		}
		mainMenuReadiness.atMainMenu = mainMenuReadiness.scene == "HUB" || mainMenuReadiness.boxState.StartsWith("HUB", StringComparison.Ordinal);
		int ready;
		if (mainMenuReadiness.atMainMenu && mainMenuReadiness.boxState == "HUB_WITH_DRAWER" && IsNetworkLoggedIn())
		{
			object obj2 = TryInvokeNoArgValue(instance, "IsSceneLoaded");
			if (obj2 is bool && (bool)obj2)
			{
				obj2 = TryInvokeNoArgValue(instance, "IsTransitionNowOrPending");
				if (obj2 is bool && !(bool)obj2)
				{
					obj2 = InvokeStaticNoArg("GameUtils", "IsAnyTransitionActive");
					ready = ((obj2 is bool && !(bool)obj2) ? 1 : 0);
					goto IL_0141;
				}
			}
		}
		ready = 0;
		goto IL_0141;
		IL_0141:
		mainMenuReadiness.ready = (byte)ready != 0;
		if (!mainMenuReadiness.ready && (IsStartScreenBoxState(mainMenuReadiness.boxState) || mainMenuReadiness.scene == "LOGIN"))
		{
			Component val = FindStartupBlockingPopup();
			mainMenuReadiness.startupPopupId = ((!((Object)(object)val == (Object)null)) ? ((Object)val).GetInstanceID() : 0);
			mainMenuReadiness.startupPopupKind = (((Object)(object)val == (Object)null) ? "" : ((object)val).GetType().Name);
			mainMenuReadiness.startScreen = (Object)(object)val == (Object)null && (mainMenuReadiness.boxState == "PRESS_START" || (Object)(object)FindChinaStartClickTarget() != (Object)null);
			mainMenuReadiness.startupPending = !mainMenuReadiness.startScreen;
		}
		if (mainMenuReadiness.ready)
		{
			object instance2 = InvokeNoArg(GetRewardTrackManager(), "GetRewardPresenter");
			mainMenuReadiness.rewardPending = TryInvokeBool(instance2, "HasReward") || TryInvokeBool(instance2, "IsShowingReward");
		}
		ReadReturningStartupReadiness(mainMenuReadiness);
		return mainMenuReadiness;
	}


	private static Component FindStartupBlockingPopup()
	{
		GameObject val = GameObject.Find("/OverlayUI(Clone)/UICanvasHeightScale/Center/CardListPopup(Clone)");
		if ((Object)(object)val != (Object)null && val.activeInHierarchy)
		{
			Component val2 = val.GetComponents<Component>().FirstOrDefault((Component c) => (Object)(object)c != (Object)null && ((object)c).GetType().Name == "CardListPopup");
			if ((Object)(object)val2 != (Object)null)
			{
				object obj = InvokeNoArg(val2, "IsShown");
				if (obj is bool && (bool)obj)
				{
					return val2;
				}
			}
		}
		Component val3 = FindCardReplacementPopup();
		if ((Object)(object)val3 != (Object)null)
		{
			return val3;
		}
		string[] array = new string[4] { "Hearthstone.Progression.QuestNotificationPopup", "Hearthstone.InGameMessage.UI.MessageModal", "EventEndedPopup", "Hearthstone.Progression.RewardScroll" };
		foreach (string name in array)
		{
			Type type = FindLoadedComponentType(name);
			if (!(type == null))
			{
				Component val4 = Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault((Component c) => (Object)(object)c != (Object)null && IsVisiblePopupComponent(c.gameObject, name));
				if ((Object)(object)val4 != (Object)null)
				{
					return val4;
				}
			}
		}
		return null;
	}


	private static GameObject FindChinaStartClickTarget()
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Expected Obj, but got Unknown
		GameObject val = GameObject.Find("/OverlayUI(Clone)/UICanvasHeightScale/Center");
		if ((Object)(object)val == (Object)null)
		{
			return null;
		}
		foreach (Transform item in val.transform)
		{
			Transform val2 = item;
			if (((Object)val2).name.StartsWith("China_Ratings_SplashScreen.prefab:", StringComparison.Ordinal))
			{
				Transform val3 = val2.Find("China_Ratings_SplashScreen/FullscreenClickCatcher");
				GameObject val4 = ((val3 != null) ? ((Component)val3).gameObject : null);
				if ((Object)(object)val4 != (Object)null && val4.activeInHierarchy)
				{
					return val4;
				}
			}
		}
		return null;
	}


	private BridgeResponse DismissStartupPopup(BridgeRequest request)
	{
		MainMenuReadiness mainMenuReadiness = ReadMainMenuReadiness();
		if (mainMenuReadiness.activeGameplay || !mainMenuReadiness.startupPending || mainMenuReadiness.startScreen || mainMenuReadiness.startupPopupId != GetInt(request.ArgumentsJson, "expectedPopupId", 0))
		{
			return BridgeResponse.Success(new
			{
				deferred = true
			});
		}
		if (mainMenuReadiness.startupPopupId == 0)
		{
			BridgeResponse bridgeResponse = DismissInGameMessageModal();
			if (bridgeResponse.Ok)
			{
				object obj = ReadMember(bridgeResponse.Data, "visible");
				if (!(obj is bool) || !(bool)obj)
				{
					BridgeResponse bridgeResponse2 = DismissNavigationPopup(request);
					if (bridgeResponse2.Ok)
					{
						obj = ReadMember(bridgeResponse2.Data, "visible");
						if (!(obj is bool) || !(bool)obj)
						{
							return DismissRewardPopup(new BridgeRequest
							{
								Command = "dismissRewardPopup",
								ArgumentsJson = "{\"requireMainMenuReady\":true,\"includeEndOfGameXp\":false}"
							});
						}
					}
					return bridgeResponse2;
				}
			}
			return bridgeResponse;
		}
		Component val = FindStartupBlockingPopup();
		if ((Object)(object)val == (Object)null || ((Object)val).GetInstanceID() != mainMenuReadiness.startupPopupId)
		{
			return BridgeResponse.Success(new
			{
				deferred = true
			});
		}
		if (mainMenuReadiness.startupPopupKind == "RedundantNDEPopup")
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(TryAcceptCardReplacementPopup()));
		}
		if (mainMenuReadiness.startupPopupKind == "RewardScroll")
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(DismissRewardScrollPopupCore(requireInitialized: true)));
		}
		string methodName = null;
		string error = null;
		Transform val2 = val.transform.Find("OkayButton");
		GameObject val3 = ((val2 != null) ? ((Component)val2).gameObject : null);
		if ((Object)(object)val3 != (Object)null && val3.activeInHierarchy)
		{
			methodName = TryInvokePegUiPressRelease(val3) ?? TryInvokePegUiClick(val3);
		}
		if (methodName == null && mainMenuReadiness.startupPopupKind != "CardListPopup")
		{
			string name = ((mainMenuReadiness.startupPopupKind == "MessageModal") ? "OnClosePressed" : "Hide");
			if (!TryInvokeZeroArgMethod(val, name, out methodName, out error))
			{
				methodName = null;
			}
		}
		return BridgeResponse.Success(new
		{
			visible = true,
			dismissed = (methodName != null),
			kind = mainMenuReadiness.startupPopupKind,
			method = (methodName ?? ""),
			error = (error ?? "")
		});
	}


	private static Component FindReturningWelcomeBanner()
	{
		GameObject root = GameObject.Find("/OverlayUI(Clone)/UICanvasHeightScale/Center/WoodenSign_Paint_Welcome_Back(Clone)");
		if ((Object)(object)root == (Object)null || !root.activeInHierarchy)
		{
			return null;
		}
		return root.GetComponents<Component>().FirstOrDefault((Component c) =>
		{
			if ((Object)(object)c != (Object)null && ReturningWelcomeBannerPolicy.IsCandidate(((object)c).GetType().FullName, GetPath(root)))
			{
				object obj = ReadMember(c, "m_onCloseCallbackCalled");
				if (obj is bool)
				{
					return !(bool)obj;
				}
				return false;
			}
			return false;
		});
	}


	private static Component FindReturningDeckPicker()
	{
		GameObject val = GameObject.Find("/Tournament(Clone)/DeckPickerTray(Clone)");
		if (!((Object)(object)val == (Object)null))
		{
			return val.GetComponents<Component>().FirstOrDefault((Component c) => (Object)(object)c != (Object)null && ((object)c).GetType().Name == "DeckPickerTrayDisplay");
		}
		return null;
	}


	private void ReadReturningStartupReadiness(MainMenuReadiness state)
	{
		object instance = InvokeStaticNoArg("ReturningPlayerMgr", "Get");
		state.returningPlayerStatus = ReadMember(instance, "m_returningPlayerStatus")?.ToString() ?? "";
		object obj = ReadMember(instance, "IsInReturningPlayerMode");
		state.isReturningPlayer = obj is bool && (bool)obj;
		if (state.isReturningPlayer)
		{
			Component val = FindReturningWelcomeBanner();
			state.returningWelcomeBannerId = ((!((Object)(object)val == (Object)null)) ? ((Object)val).GetInstanceID() : 0);
			if (state.returningWelcomeBannerId != 0)
			{
				state.ready = false;
			}
			string text = ((!(state.scene == "TOURNAMENT")) ? "" : (ReadMember(FindReturningDeckPicker(), "m_setRotationTutorialState")?.ToString() ?? ""));
			state.returningStartupPending = !state.startScreen && (state.returningWelcomeBannerId != 0 || state.startupPending || state.boxState.StartsWith("SET_ROTATION", StringComparison.Ordinal) || text == "READY" || _setRotationReturnInFlight || (state.atMainMenu && !state.ready));
			state.returningStage = $"{state.boxState}:{text}:{state.startupPopupId}:{_setRotationReturnInFlight}:welcome={state.returningWelcomeBannerId}";
		}
	}


	private object ReadReturningPlayerState()
	{
		return ReadMainMenuReadiness();
	}


	private BridgeResponse AdvanceReturningPlayerStartup(BridgeRequest request)
	{
		MainMenuReadiness mainMenuReadiness = ReadMainMenuReadiness();
		if (mainMenuReadiness.activeGameplay || !mainMenuReadiness.isReturningPlayer || mainMenuReadiness.startScreen || mainMenuReadiness.ready || mainMenuReadiness.returningStage != GetString(request.ArgumentsJson, "expectedStage"))
		{
			return BridgeResponse.Success(new
			{
				deferred = true,
				isReturningPlayer = mainMenuReadiness.isReturningPlayer
			});
		}
		if (mainMenuReadiness.returningWelcomeBannerId != 0)
		{
			Component val = FindReturningWelcomeBanner();
			if (!((Object)(object)val == (Object)null) && ((Object)val).GetInstanceID() == mainMenuReadiness.returningWelcomeBannerId && _closedReturningWelcomeBannerId != mainMenuReadiness.returningWelcomeBannerId)
			{
				object obj = ReadMember(val, "m_showSpellComplete");
				if (obj is bool && (bool)obj && ReadMember(val, "m_onCloseBannerPopup") is Delegate)
				{
					if (!TryInvokeZeroArgMethod(val, "Close", out var methodName, out var error))
					{
						return BridgeResponse.Failure(error);
					}
					_closedReturningWelcomeBannerId = mainMenuReadiness.returningWelcomeBannerId;
					return BridgeResponse.Success(new
					{
						visible = true,
						dismissed = true,
						kind = "ReturningWelcomeBanner",
						method = methodName
					});
				}
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				pending = true,
				dismissed = false,
				kind = "ReturningWelcomeBanner"
			});
		}
		SetRotationTransitionResult setRotationTransitionResult = TryHandleSetRotationTransitionCore();
		if (setRotationTransitionResult.Failed)
		{
			return BridgeResponse.Failure(setRotationTransitionResult.Error);
		}
		if (setRotationTransitionResult.Visible)
		{
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = setRotationTransitionResult.Dismissed,
				pending = setRotationTransitionResult.Pending,
				kind = "SetRotationTransition",
				stage = setRotationTransitionResult.Stage,
				method = setRotationTransitionResult.Method,
				error = setRotationTransitionResult.Error
			});
		}
		BridgeResponse bridgeResponse = DismissInGameMessageModal();
		if (bridgeResponse.Ok)
		{
			object obj = ReadMember(bridgeResponse.Data, "visible");
			if (!(obj is bool) || !(bool)obj)
			{
				BridgeResponse bridgeResponse2 = DismissNavigationPopup(request);
				if (bridgeResponse2.Ok)
				{
					obj = ReadMember(bridgeResponse2.Data, "visible");
					if (!(obj is bool) || !(bool)obj)
					{
						return DismissRewardPopup(new BridgeRequest
						{
							Command = "dismissRewardPopup",
							ArgumentsJson = "{\"requireMainMenuReady\":true,\"includeEndOfGameXp\":false}"
						});
					}
				}
				return bridgeResponse2;
			}
		}
		return bridgeResponse;
	}


	private RewardPopupDismissResult DismissRotatedBoostersPopup()
	{
		RewardPopupDismissResult rewardPopupDismissResult = new RewardPopupDismissResult
		{
			Kind = "SetRotationRotatedBoostersPopup"
		};
		if (IsActiveGameInProgress())
		{
			return rewardPopupDismissResult;
		}
		GameObject val = GameObject.Find("/OverlayUI(Clone)/UICanvasHeightScale/Center/SetRotationRotatedBoostersPopup Popup Bone/SetRotationRotatedBoostersPopup");
		Component val2 = (((Object)(object)val == (Object)null) ? null : val.GetComponents<Component>().FirstOrDefault((Component c) => (Object)(object)c != (Object)null && ((object)c).GetType().Name == "SetRotationRotatedBoostersPopup"));
		if ((Object)(object)val2 == (Object)null || !IsVisiblePopupComponent(val, rewardPopupDismissResult.Kind))
		{
			_closingRotatedBoostersPopup = null;
			return rewardPopupDismissResult;
		}
		rewardPopupDismissResult.Visible = true;
		if (_closingRotatedBoostersPopup == val2)
		{
			return rewardPopupDismissResult;
		}
		if (TryInvokeZeroArgMethod(val2, "Hide", out var methodName, out var error))
		{
			_closingRotatedBoostersPopup = val2;
			rewardPopupDismissResult.Dismissed = true;
			rewardPopupDismissResult.Method = methodName;
			rewardPopupDismissResult.Target = Describe(val, includeComponents: false);
		}
		else
		{
			rewardPopupDismissResult.Error = error;
		}
		return rewardPopupDismissResult;
	}


	private object FindObjects(BridgeRequest request)
	{
		Selector selector = Selector.From(ExtractObject(request.ArgumentsJson, "selector"));
		int val = GetInt(request.ArgumentsJson, "maxObjects", 100);
		bool includeComponents = GetBool(request.ArgumentsJson, "includeComponents", defaultValue: true);
		object[] array = (from gameObject in FindMatchingObjects(selector).Take(Math.Max(1, val))
			select Describe(gameObject, includeComponents)).ToArray();
		return new
		{
			count = array.Length,
			objects = array
		};
	}


	private object BuildBattlegroundsStateLite(BridgeRequest request)
	{
		//IL_0769: Unknown result type (might be due to invalid IL or missing references)
		//IL_076e: Unknown result type (might be due to invalid IL or missing references)
		//IL_1493: Unknown result type (might be due to invalid IL or missing references)
		//IL_1498: Unknown result type (might be due to invalid IL or missing references)
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num = GetInt(request.ArgumentsJson, "maxCards", 2200);
		string a = GetString(request.ArgumentsJson, "mode");
		bool flag = string.Equals(a, "Battlegrounds", StringComparison.OrdinalIgnoreCase);
		bool flag2 = flag || string.Equals(a, "Constructed", StringComparison.OrdinalIgnoreCase);
		bool includeConstructedState = string.Equals(a, "Constructed", StringComparison.OrdinalIgnoreCase);
		bool flag3 = includeConstructedState && GetBool(request.ArgumentsJson, "ai3Diagnostics", defaultValue: false);
		bool nativeTradeTags = includeConstructedState && GetBool(request.ArgumentsJson, "nativeTradeTags", defaultValue: false);
		Dictionary<string, long> phaseTimes = (flag3 ? new Dictionary<string, long>() : null);
		long phaseStartedMs = 0L;
		bool flag4 = flag && GetBool(request.ArgumentsJson, "activeBattlegroundsOnly", defaultValue: false);
		List<object> list = new List<object>();
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Component val = null;
		Component val2 = null;
		Component val3 = null;
		Component val4 = null;
		Component val5 = null;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		bool flag5 = false;
		bool flag6 = false;
		bool flag7 = false;
		bool mulliganIntroStateAvailable = false;
		bool mulliganIntroComplete = false;
		bool mulliganIntroRunning = false;
		bool mulliganCardsTransitioning = false;
		bool flag8 = false;
		bool flag9 = false;
		bool flag10 = false;
		bool flag11 = false;
		bool flag12 = false;
		GameObject val6 = null;
		GameObject val7 = null;
		GameObject val8 = null;
		object gameState = GetGameState();
		object obj = InvokeNoArg(gameState, "GetGameEntity");
		object obj2 = InvokeNoArg(gameState, "GetFriendlySidePlayer") ?? InvokeNoArg(gameState, "GetLocalSidePlayer");
		object entity = InvokeNoArg(gameState, "GetOpposingSidePlayer") ?? InvokeNoArg(gameState, "GetOpponentSidePlayer");
		object instance = InvokeNoArg(gameState, "GetCurrentPlayer");
		int? num8 = TryReadAvailableResources(gameState);
		int? num9 = ReadEntityTagInt(obj2, "RESOURCES");
		int battlegroundsTavernTier = ((!flag) ? 1 : Math.Max(1, ReadEntityTagInt(obj2, "PLAYER_TECH_LEVEL") ?? 1));
		int num10 = ReadIntMember(gameState, "GetTurn", 0);
		bool flag13 = TryInvokeBool(obj, "IsInBattlegroundsCombatPhase");
		bool flag14 = TryInvokeBool(obj, "IsInBattlegroundsShopPhase");
		int valueOrDefault = ReadEntityTagInt(obj2, "PLAYSTATE").GetValueOrDefault();
		int valueOrDefault2 = ReadEntityTagInt(entity, "PLAYSTATE").GetValueOrDefault();
		int friendlyPlayerId = ReadIntMember(gameState, "GetFriendlyPlayerId", 0);
		int battlegroundsTutorialGameId = (flag ? ReadBattlegroundsTutorialId(obj, friendlyPlayerId) : 0);
		int battlegroundsRating = (flag ? ReadBattlegroundsRating() : (-1));
		bool flag15 = GetBool(request.ArgumentsJson, "minimal", defaultValue: false);
		bool flag16 = GetBool(request.ArgumentsJson, "includeNeteasePredictorMetadata", defaultValue: false);
		int[] battlegroundsAvailableRaces = (((flag && !flag15) & flag16) ? ReadBattlegroundsAvailableRaces(gameState) : Array.Empty<int>());
		bool flag17 = flag15 && GetBool(request.ArgumentsJson, "pulse", defaultValue: false);
		bool flag18 = flag;
		if (flag18)
		{
			bool flag19 = (((uint)(valueOrDefault - 4) <= 2u || valueOrDefault == 8) ? true : false);
			flag18 = flag19;
		}
		bool flag20 = flag18;
		Component val9 = null;
		if (flag)
		{
			val9 = ProbeVisibleBattlegroundsEndGameScreen(force: false);
			flag11 = flag20 || (Object)(object)val9 != (Object)null;
		}
		int battlegroundsPlacement = 0;
		string friendlyHeroCardId = "";
		string friendlyHeroName = "";
		bool battlegroundsHeroesReady = false;
		if (flag && !flag15)
		{
			int num11 = ReadEntityTagInt(obj2, "HERO_ENTITY") ?? ReadIntMember(gameState, "GetFriendlySideHeroEntityId", 0);
			int num12 = ReadEntityTagInt(entity, "HERO_ENTITY") ?? ReadIntMember(gameState, "GetOpposingSideHeroEntityId", 0);
			battlegroundsHeroesReady = num11 > 0 && num12 > 0;
			object instance2 = ((num11 > 0) ? (InvokeIntArgObject(gameState, "GetEntity", num11) ?? InvokeIntArgObject(gameState, "GetEntityByID", num11)) : null);
			friendlyHeroCardId = InvokeNoArg(instance2, "GetCardId")?.ToString() ?? "";
			friendlyHeroName = InvokeNoArg(instance2, "GetName")?.ToString() ?? "";
			battlegroundsPlacement = ReadBattlegroundsPlacement(obj2, val9);
		}
		if ((Object)(object)val9 != (Object)null && TryGetGameObject(val9, out var gameObject))
		{
			val6 = gameObject;
		}
		flag18 = includeConstructedState;
		if (flag18)
		{
			bool flag19 = TryInvokeBool(gameState, "IsGameOver");
			if (!flag19)
			{
				bool flag21 = (((uint)(valueOrDefault - 4) <= 2u || valueOrDefault == 8) ? true : false);
				flag19 = flag21;
			}
			flag18 = flag19;
		}
		flag12 = flag18;
		Scene activeScene;
		if (flag15 || (flag13 && !flag20))
		{
			int num13 = Math.Max(0, num9.GetValueOrDefault());
			int num14 = Math.Max(0, num8.GetValueOrDefault());
			object obj3;
			if (!flag17 || flag13)
			{
				obj3 = null;
			}
			else
			{
				object obj4 = InvokeStaticNoArg("ChoiceCardMgr", "Get");
				obj3 = ((obj4 is Component) ? obj4 : null);
			}
			Component val10 = (Component)obj3;
			bool flag22 = (Object)(object)val10 != (Object)null && (IsFriendlyChoicesShown(val10) || TryInvokeBool(val10, "HasFriendlyChoices"));
			int num15 = (flag22 ? ReadChoiceCardObjects(val10).Count() : 0);
			bool flag23 = flag22 && TryInvokeBool(val10, "IsFriendlyMagicItemDiscover");
			object obj5;
			if (!flag17)
			{
				obj5 = null;
			}
			else
			{
				object obj6 = InvokeStaticNoArg("MulliganManager", "Get");
				obj5 = ((obj6 is Component) ? obj6 : null);
			}
			Component val11 = (Component)obj5;
			bool flag24 = (Object)(object)val11 != (Object)null && ReadMemberAsBool(val11, "m_waitingForUserInput", defaultValue: false);
			bool flag25 = false;
			if ((Object)(object)val11 != (Object)null)
			{
				object obj7 = InvokeNoArg(val11, "GetMulliganButton");
				GameObject val12 = (GameObject)((obj7 is GameObject) ? obj7 : null);
				if (val12 != null)
				{
					flag25 = val12.activeInHierarchy;
				}
				else
				{
					Component val13 = (Component)((obj7 is Component) ? obj7 : null);
					if (val13 != null && TryGetGameObject(val13, out var gameObject2))
					{
						flag25 = gameObject2.activeInHierarchy;
					}
				}
			}
			bool flag26 = flag17 && (flag24 | flag25);
			bool flag27 = flag22 && num15 > 0 && !flag23;
			bool hasChoicePrompt = flag26 | flag27 | flag23;
			object instance3 = (flag17 ? InvokeNoArg(gameState, "GetOptionsPacket") : null);
			IEnumerable enumerable = InvokeNoArg(instance3, "get_List") as IEnumerable;
			bool optionsPacketAvailable = enumerable != null;
			int num16 = enumerable?.Cast<object>().Count() ?? 0;
			int num17 = (flag17 ? ReadIntMember(instance3, "get_ID", 0) : 0);
			object obj8 = (flag17 ? InvokeStaticNoArg("TurnTimer", "Get") : null);
			float num18 = ReadTurnTimerRemainingSeconds(obj8);
			bool flag28 = obj8 != null && TryInvokeBool(obj8, "IsRopeActive");
			string stateRevision = string.Join("|", num10, flag13 ? 1 : 0, flag14 ? 1 : 0, num14, num13, num17, num16, num15, flag26 ? 1 : 0, (num18 >= 0f && num18 <= 10f) ? 1 : 0, flag28 ? 1 : 0, flag11 ? 1 : 0, valueOrDefault, valueOrDefault2);
			stopwatch.Stop();
			activeScene = SceneManager.GetActiveScene();
			return new
			{
				scene = activeScene.name,
				frame = Time.frameCount,
				gameTurn = num10,
				battlegroundsTavernTier = battlegroundsTavernTier,
				battlegroundsRating = battlegroundsRating,
				battlegroundsAvailableRaces = battlegroundsAvailableRaces,
				battlegroundsPlacement = battlegroundsPlacement,
				friendlyHeroCardId = friendlyHeroCardId,
				friendlyHeroName = friendlyHeroName,
				battlegroundsHeroesReady = battlegroundsHeroesReady,
				battlegroundsTutorialGameId = battlegroundsTutorialGameId,
				elapsedMs = stopwatch.ElapsedMilliseconds,
				objectCount = 0,
				componentScanCount = 0,
				textScanCount = 0,
				cardCount = 0,
				choiceCardCount = num15,
				minimalState = true,
				pulseState = flag17,
				stateRevision = stateRevision,
				mana = new
				{
					total = num13,
					ready = num14,
					visualReady = num14,
					used = Math.Max(0, num13 - num14),
					proposed = 0,
					source = (num8.HasValue ? "Player.GetNumAvailableResources" : "Player.RESOURCES")
				},
				turnOwner = new
				{
					friendlyPlayerId = ReadIntMember(gameState, "GetFriendlyPlayerId", 0),
					opposingPlayerId = ReadIntMember(gameState, "GetOpposingPlayerId", 0),
					currentPlayerId = ReadIntMember(instance, "GetPlayerId", 0),
					isFriendlySidePlayerTurn = TryInvokeBool(gameState, "IsFriendlySidePlayerTurn"),
					isLocalSidePlayerTurn = TryInvokeBool(gameState, "IsLocalSidePlayerTurn")
				},
				turnStart = (object)null,
				turnTimer = ((obj8 == null) ? null : new
				{
					state = (ReadMemberAsString(obj8, "m_state") ?? ""),
					countdownTimeoutSec = ReadMemberAsInt(obj8, "m_countdownTimeoutSec", 0),
					remainingSec = num18,
					friendlySidePlayer = ReadMemberAsBool(obj8, "m_currentTimerBelongsToFriendlySidePlayer", defaultValue: false),
					waitingForTurnStartManager = ReadMemberAsBool(obj8, "m_waitingForTurnStartManagerFinish", defaultValue: false),
					ropeActive = flag28
				}),
				endTurnButton = (object)null,
				hasRecruitText = false,
				isBattlegroundsCombatPhase = flag13,
				isBattlegroundsShopPhase = flag14,
				hasChoicePrompt = hasChoicePrompt,
				friendlyChoicesPending = flag22,
				hasHeroChoicePrompt = flag26,
				hasMulliganConfirmButton = flag25,
				hasMatchingPopup = false,
				isMulliganWaitingForUserInput = flag24,
				hasStandardChoicePrompt = flag27,
				hasTrinketShopPrompt = false,
				hasTrinketChoicePrompt = flag23,
				optionsPacketAvailable = optionsPacketAvailable,
				optionsPacketCount = num16,
				optionsPacketId = num17,
				hasBattlegroundsEndGameScreen = flag11,
				hasConstructedEndGameScreen = false,
				friendlyPlayState = valueOrDefault,
				opposingPlayState = valueOrDefault2,
				constructedResult = ResolveConstructedResult(valueOrDefault, valueOrDefault2, null),
				endGameScreen = (object)null,
				constructedEndGameScreen = (object)null,
				trinketConfirmButton = (object)null,
				lockOverlayPaths = Array.Empty<string>(),
				lockedHeroEntityIds = Array.Empty<string>(),
				objects = Array.Empty<object>(),
				choiceCards = Array.Empty<object>(),
				constructedOptionsPacketId = num17,
				constructedOptions = Array.Empty<object>()
			};
		}
		HashSet<int> hashSet2 = new HashSet<int>();
		MeasurePhase("header");
		int entityMapCount = 0;
		Component[] components = Array.Empty<Component>();
		string scanSource = "";
		bool useDirectEntityScan = flag2 && TryFindDirectGameplayStateComponents(gameState, flag4 ? int.MaxValue : num, out components, out entityMapCount, out scanSource);
		int unfilteredDirectCardCount = (useDirectEntityScan ? Enumerable.Count(components, (Component component) => (Object)(object)component != (Object)null && string.Equals(((object)component).GetType().Name, "Card", StringComparison.Ordinal)) : 0);
		if (useDirectEntityScan & flag4)
		{
			components = FilterCurrentBattlegroundsStateComponents(components, friendlyPlayerId);
			components = LimitDirectCardComponents(components, num);
		}
		bool flag29 = useDirectEntityScan | flag14;
		Component[] array;
		if (useDirectEntityScan)
		{
			array = components;
		}
		else
		{
			array = (flag29 ? FindBattlegroundsStateComponents() : Object.FindObjectsOfType<Component>());
		}
		int componentScanCount = array.Length;
		MeasurePhase("components");
		Component[] array2 = array;
		foreach (Component val14 in array2)
		{
			if (!TryGetGameObject(val14, out var gameObject3) || !IsBridgeVisibleObject(gameObject3))
			{
				continue;
			}
			if (hashSet2.Add(((Object)gameObject3).GetInstanceID()))
			{
				num2++;
				string text = ((Object)gameObject3).name ?? "";
				if (text.IndexOf("TrinketShop", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("BaconTrinket", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					flag10 = true;
				}
				if (gameObject3.activeInHierarchy && (text.IndexOf("BaconTwoScoop", StringComparison.OrdinalIgnoreCase) >= 0 || IsBattlegroundsEndGamePlacementObject(gameObject3)))
				{
					flag11 = true;
					if (val6 == null)
					{
						val6 = gameObject3;
					}
				}
				if (gameObject3.activeInHierarchy && IsConstructedEndGameObject(gameObject3))
				{
					flag12 = true;
					if (val7 == null)
					{
						val7 = gameObject3;
					}
				}
				if ((Object)(object)val8 == (Object)null && gameObject3.activeInHierarchy && ContainsAny(text, "BGTrinketConfirmChoiceButton", "TrinketConfirm", "ConfirmChoiceButton"))
				{
					val8 = gameObject3;
				}
				flag8 |= ContainsAny(text, "MatchingPopup", "Matchmaking");
				if (gameObject3.activeInHierarchy && (IsLockOverlayName(text) || string.Equals(text, "Locked hero bg new", StringComparison.Ordinal)))
				{
					string path = GetPath(gameObject3);
					list2.Add(path);
					if (string.Equals(text, "Locked hero bg new", StringComparison.Ordinal))
					{
						string text2 = TryReadEntityIdFromPath(path);
						if (!string.IsNullOrWhiteSpace(text2))
						{
							hashSet.Add(text2);
						}
					}
				}
				if (gameObject3.activeInHierarchy && (string.Equals(text, "ConfirmButton", StringComparison.Ordinal) || string.Equals(text, "BlueButtonContainer", StringComparison.Ordinal)))
				{
					string path2 = GetPath(gameObject3);
					flag7 |= path2.IndexOf("/Gameplay/MulliganManager(Clone)/ConfirmButton", StringComparison.OrdinalIgnoreCase) >= 0;
				}
			}
			string name = ((object)val14).GetType().Name;
			if (gameObject3.activeInHierarchy && string.Equals(name, "BaconEndGameScreen", StringComparison.Ordinal))
			{
				flag11 = true;
				if (val6 == null)
				{
					val6 = gameObject3;
				}
			}
			if (string.Equals(name, "Card", StringComparison.Ordinal))
			{
				if (list.Count < num)
				{
					list.Add(useDirectEntityScan ? DescribeLiteCardFast(gameObject3, val14, gameState, friendlyPlayerId, includeConstructedState, nativeTradeTags) : DescribeLiteCard(gameObject3, val14));
				}
				if (flag)
				{
					AppendActiveHandLockOverlayPaths(gameObject3, val14, friendlyPlayerId, list2);
				}
				continue;
			}
			if ((Object)(object)val == (Object)null && string.Equals(name, "ChoiceCardMgr", StringComparison.Ordinal) && string.Equals(((Object)gameObject3).name, "ChoiceCardMgr(Clone)", StringComparison.Ordinal))
			{
				val = val14;
				continue;
			}
			if ((Object)(object)val2 == (Object)null && string.Equals(name, "MulliganManager", StringComparison.Ordinal) && string.Equals(((Object)gameObject3).name, "MulliganManager(Clone)", StringComparison.Ordinal))
			{
				val2 = val14;
			}
			if ((Object)(object)val3 == (Object)null && string.Equals(name, "TurnStartManager", StringComparison.Ordinal) && string.Equals(((Object)gameObject3).name, "TurnStartManager(Clone)", StringComparison.Ordinal))
			{
				val3 = val14;
			}
			if ((Object)(object)val4 == (Object)null && string.Equals(name, "TurnTimer", StringComparison.Ordinal) && string.Equals(((Object)gameObject3).name, "TurnTimer(Clone)", StringComparison.Ordinal))
			{
				val4 = val14;
			}
			if ((Object)(object)val5 == (Object)null && string.Equals(name, "EndTurnButton", StringComparison.Ordinal))
			{
				val5 = val14;
			}
			if (string.Equals(name, "ManaCrystal", StringComparison.Ordinal) && ((Object)gameObject3).name.IndexOf("Resource_Coin", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				num4++;
				string text3 = ReadMemberAsString(val14, "state") ?? ReadMemberAsString(val14, "m_state") ?? "";
				string text4 = ReadMemberAsString(val14, "m_visibleState") ?? "";
				if (string.Equals(text3, "READY", StringComparison.OrdinalIgnoreCase) || string.Equals(text4, "READY", StringComparison.OrdinalIgnoreCase))
				{
					num5++;
				}
				else if (string.Equals(text3, "USED", StringComparison.OrdinalIgnoreCase) || string.Equals(text4, "USED", StringComparison.OrdinalIgnoreCase))
				{
					num6++;
				}
				else if (text3.IndexOf("PROPOSED", StringComparison.OrdinalIgnoreCase) >= 0 || text4.IndexOf("PROPOSED", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					num7++;
				}
			}
			else
			{
				if (!gameObject3.activeInHierarchy || flag29)
				{
					continue;
				}
				string text5 = ReadLikelyText(val14);
				if (!string.IsNullOrWhiteSpace(text5))
				{
					num3++;
					flag5 |= text5.IndexOf("招揽随从", StringComparison.OrdinalIgnoreCase) >= 0;
					flag6 |= ContainsAny(text5, "选择你的英雄", "选择一位英雄", "选择一个英雄");
					flag8 |= ContainsAny(text5, "正在寻找对手", "正在匹配", "寻找对手", "匹配中");
					flag9 |= ContainsAny(text5, "进行选择", "选择一个", "选择一项", "发现");
					flag10 |= ContainsAny(text5, "饰品商店", "选择一项并购买");
					if (ContainsAny(text5, "锁", "不能"))
					{
						list2.Add(GetPath(gameObject3));
					}
				}
			}
		}
		MeasurePhase("cards");
		bool result = default;
		bool flag30 = ((Object)(object)val2 != (Object)null && bool.TryParse(ReadMemberAsString(val2, "m_waitingForUserInput"), out result)) & result;
		if ((Object)(object)val2 != (Object)null)
		{
			object obj9 = ReadMember(val2, "introComplete") ?? ReadMember(val2, "m_introComplete");
			mulliganIntroStateAvailable = obj9 != null;
			bool result2 = default;
			mulliganIntroComplete = (obj9 != null && bool.TryParse(obj9.ToString(), out result2)) & result2;
			mulliganIntroRunning = TryInvokeBool(val2, "IsMulliganIntroRunning") || ReadMemberAsBool(val2, "m_introRunning", defaultValue: false);
			mulliganCardsTransitioning = (MaterializeMulliganCards(InvokeNoArg(val2, "GetStartingCards")) ?? MaterializeMulliganCards(ReadMember(val2, "m_startingCards")))?.Any((object card) => card != null && ReadMemberAsBool(card, "m_transitioningZones", defaultValue: false)) ?? false;
		}
		if (useDirectEntityScan && (Object)(object)val2 != (Object)null)
		{
			object obj10 = InvokeNoArg(val2, "GetMulliganButton");
			GameObject val15 = (GameObject)((obj10 is GameObject) ? obj10 : null);
			if (val15 != null)
			{
				flag7 = val15.activeInHierarchy;
			}
			if ((Object)(object)val != (Object)null)
			{
				object obj11 = ReadMember(val, "m_confirmChoiceButton");
				Component val16 = (Component)((obj11 is Component) ? obj11 : null);
				if (val16 != null && TryGetGameObject(val16, out var gameObject4) && gameObject4.activeInHierarchy)
				{
					val8 = gameObject4;
				}
			}
		}
		if (flag29 && !includeConstructedState && (flag7 | flag30))
		{
			flag6 = true;
		}
		flag6 &= flag7 | flag30;
		int num20 = ReadMemberAsInt(val3, "m_manaCrystalsFilled", 0);
		int num21 = ReadMemberAsInt(val3, "m_manaCrystalsGained", 0);
		bool flag31 = (Object)(object)val != (Object)null && IsFriendlyChoicesShown(val);
		int num22;
		object[] array3;
		if ((Object)(object)val != (Object)null)
		{
			if (!flag31)
			{
				num22 = (TryInvokeBool(val, "HasFriendlyChoices") ? 1 : 0);
				if (num22 == 0)
				{
					goto IL_10e1;
				}
			}
			else
			{
				num22 = 1;
			}
			array3 = (from gameObject6 in ReadChoiceCardObjects(val)
				select (!useDirectEntityScan) ? TryDescribeLiteChoiceCard(gameObject6) : TryDescribeLiteChoiceCardFast(gameObject6, gameState, friendlyPlayerId, includeConstructedState) into item
				where item != null
				select item).ToArray();
			goto IL_1129;
		}
		num22 = 0;
		goto IL_10e1;
		IL_10e1:
		array3 = Array.Empty<object>();
		goto IL_1129;
		IL_1129:
		object[] array4 = array3;
		int choiceId = 0;
		int countMin = 0;
		int countMax = 0;
		int[] array5 = ReadFriendlyKerriganChoiceEntityIds(gameState, out choiceId, out countMin, out countMax);
		if (array4.Length == 0 && array5.Length != 0)
		{
			array4 = (from item in array5.Select((int entityId, int index) => TryDescribeLiteLogicalChoiceCard(gameState, entityId, index))
				where item != null
				select item).ToArray();
		}
		int choiceId2 = 0;
		int countMin2 = 0;
		int countMax2 = 0;
		int[] array6 = (includeConstructedState ? ReadFriendlyTargetChoiceEntityIds(gameState, out choiceId2, out countMin2, out countMax2) : Array.Empty<int>());
		bool flag32 = array6.Length != 0;
		bool flag33 = array5.Length != 0;
		bool friendlyChoicesPending = (byte)((uint)num22 | (flag32 ? 1u : 0u) | (flag33 ? 1u : 0u)) != 0;
		bool flag34 = flag31 && TryInvokeBool(val, "IsFriendlyMagicItemDiscover");
		bool flag35 = (flag32 | flag33) || (flag31 && !flag34 && (flag9 || array4.Length != 0));
		bool hasChoicePrompt2 = flag6 | flag35 | flag34;
		if (useDirectEntityScan & flag6)
		{
			foreach (Component item in array.Where((Component component) => (Object)(object)component != (Object)null && string.Equals(((object)component).GetType().Name, "Card", StringComparison.Ordinal)))
			{
				object obj12 = InvokeNoArg(item, "GetEntity");
				string cardId = InvokeNoArg(obj12, "GetCardId")?.ToString() ?? "";
				if (obj12 != null && IsBattlegroundsHeroCardId(cardId))
				{
					int num23 = ReadIntMember(obj12, "GetEntityId", 0);
					if (num23 > 0 && TryGetGameObject(item, out var gameObject5) && HasActiveDescendantNamed(gameObject5, "Locked hero bg new"))
					{
						hashSet.Add(num23.ToString(CultureInfo.InvariantCulture));
					}
				}
			}
		}
		MeasurePhase("choices");
		int constructedOptionsPacketId = (includeConstructedState ? ReadConstructedOptionsPacketId() : 0);
		object[] constructedOptions = (includeConstructedState ? ReadConstructedOptions() : Array.Empty<object>());
		MeasurePhase("options");
		object instance4 = InvokeNoArg(gameState, "GetOptionsPacket");
		IEnumerable enumerable2 = InvokeNoArg(instance4, "get_List") as IEnumerable;
		bool optionsPacketAvailable2 = enumerable2 != null;
		int optionsPacketId = ReadIntMember(instance4, "get_ID", 0);
		object[] array7 = ((enumerable2 == null) ? Array.Empty<object>() : (from object option in enumerable2
			where option != null
			select option).ToArray());
		int optionsPacketCount = array7.Length;
		if (flag)
		{
			object obj13 = ReadBattlegroundsDarkGiftOptionCard(array7, gameState, friendlyPlayerId);
			if (obj13 != null)
			{
				list.Add(obj13);
			}
		}
		object[] battlegroundsOptionTargets = (flag ? ReadBattlegroundsOptionTargets(array7, gameState, friendlyPlayerId) : Array.Empty<object>());
		int[] battlegroundsBuyTargetEntityIds = (flag ? ReadBattlegroundsBuyTargetEntityIds(array7, gameState) : Array.Empty<int>());
		int[] battlegroundsBuyMinionTargetEntityIds = (flag ? ReadBattlegroundsBuyTargetEntityIds(array7, gameState, "TB_BaconShop_DragBuy") : Array.Empty<int>());
		int[] battlegroundsBuySpecialTargetEntityIds = (flag ? ReadBattlegroundsBuyTargetEntityIds(array7, gameState, "TB_BaconShop_DragBuy_Spell") : Array.Empty<int>());
		object[] battlegroundsPlayerRatings = ((flag16 & flag6) ? ReadBattlegroundsPlayerRatings(gameState) : Array.Empty<object>());
		object[] battlegroundsPlayerRaceCounts = (((flag16 & flag) && !flag15) ? ReadBattlegroundsPlayerRaceCounts() : Array.Empty<object>());
		string constructedResult = ResolveConstructedResult(valueOrDefault, valueOrDefault2, val7);
		stopwatch.Stop();
		activeScene = SceneManager.GetActiveScene();
		return new
		{
			scene = activeScene.name,
			frame = Time.frameCount,
			gameTurn = num10,
			battlegroundsTavernTier = battlegroundsTavernTier,
			battlegroundsRating = battlegroundsRating,
			battlegroundsAvailableRaces = battlegroundsAvailableRaces,
			battlegroundsPlacement = battlegroundsPlacement,
			friendlyHeroCardId = friendlyHeroCardId,
			friendlyHeroName = friendlyHeroName,
			battlegroundsHeroesReady = battlegroundsHeroesReady,
			battlegroundsTutorialGameId = battlegroundsTutorialGameId,
			elapsedMs = stopwatch.ElapsedMilliseconds,
			objectCount = num2,
			componentScanCount = componentScanCount,
			entityMapCount = entityMapCount,
			ai3PhaseTimes = phaseTimes,
			unfilteredDirectCardCount = unfilteredDirectCardCount,
			directEntityScan = useDirectEntityScan,
			activeBattlegroundsOnly = flag4,
			scanSource = (useDirectEntityScan ? scanSource : "Unity scene components"),
			textScanCount = num3,
			cardCount = list.Count,
			choiceCardCount = array4.Length,
			friendlyTargetChoiceCount = array6.Length,
			mana = new
			{
				total = Math.Max(num4, num9.GetValueOrDefault()),
				ready = (num8 ?? num5),
				visualReady = num5,
				used = num6,
				proposed = num7,
				source = (num8.HasValue ? "Player.GetNumAvailableResources" : "ManaCrystal")
			},
			turnOwner = new
			{
				friendlyPlayerId = ReadIntMember(gameState, "GetFriendlyPlayerId", 0),
				opposingPlayerId = ReadIntMember(gameState, "GetOpposingPlayerId", 0),
				currentPlayerId = ReadIntMember(instance, "GetPlayerId", 0),
				isFriendlySidePlayerTurn = TryInvokeBool(gameState, "IsFriendlySidePlayerTurn"),
				isLocalSidePlayerTurn = TryInvokeBool(gameState, "IsLocalSidePlayerTurn")
			},
			turnStart = (((Object)(object)val3 == (Object)null) ? null : new
			{
				manaCrystalsFilled = num20,
				manaCrystalsGained = num21,
				maxResources = num20 + num21,
				blockingInput = (ReadMemberAsBool(val3, "m_blockingInput", defaultValue: false) || TryInvokeBool(val3, "IsBlockingInput")),
				listeningForTurnEvents = (ReadMemberAsBool(val3, "m_listeningForTurnEvents", defaultValue: false) || TryInvokeBool(val3, "IsListeningForTurnEvents")),
				indicatorShowing = TryInvokeBool(val3, "IsTurnStartIndicatorShowing")
			}),
			turnTimer = (((Object)(object)val4 == (Object)null) ? null : new
			{
				state = (ReadMemberAsString(val4, "m_state") ?? ""),
				countdownTimeoutSec = ReadMemberAsInt(val4, "m_countdownTimeoutSec", 0),
				remainingSec = ReadTurnTimerRemainingSeconds(val4),
				friendlySidePlayer = ReadMemberAsBool(val4, "m_currentTimerBelongsToFriendlySidePlayer", defaultValue: false),
				waitingForTurnStartManager = ReadMemberAsBool(val4, "m_waitingForTurnStartManagerFinish", defaultValue: false),
				ropeActive = TryInvokeBool(val4, "IsRopeActive")
			}),
			endTurnButton = DescribeConstructedEndTurnButton(val5),
			hasRecruitText = flag5,
			isBattlegroundsCombatPhase = flag13,
			isBattlegroundsShopPhase = flag14,
			hasChoicePrompt = hasChoicePrompt2,
			friendlyChoicesPending = friendlyChoicesPending,
			friendlyTargetChoicePending = flag32,
			friendlyTargetChoiceId = choiceId2,
			friendlyTargetChoiceCountMin = countMin2,
			friendlyTargetChoiceCountMax = countMax2,
			friendlyKerriganChoicePending = flag33,
			friendlyKerriganChoiceId = choiceId,
			friendlyKerriganChoiceCountMin = countMin,
			friendlyKerriganChoiceCountMax = countMax,
			hasHeroChoicePrompt = flag6,
			hasMulliganConfirmButton = flag7,
			mulliganIntroStateAvailable = mulliganIntroStateAvailable,
			mulliganIntroComplete = mulliganIntroComplete,
			mulliganIntroRunning = mulliganIntroRunning,
			mulliganCardsTransitioning = mulliganCardsTransitioning,
			hasMatchingPopup = flag8,
			isMulliganWaitingForUserInput = flag30,
			hasStandardChoicePrompt = flag35,
			hasTrinketShopPrompt = flag10,
			hasTrinketChoicePrompt = flag34,
			optionsPacketAvailable = optionsPacketAvailable2,
			optionsPacketCount = optionsPacketCount,
			optionsPacketId = optionsPacketId,
			battlegroundsOptionTargets = battlegroundsOptionTargets,
			battlegroundsBuyTargetEntityIds = battlegroundsBuyTargetEntityIds,
			battlegroundsBuyMinionTargetEntityIds = battlegroundsBuyMinionTargetEntityIds,
			battlegroundsBuySpecialTargetEntityIds = battlegroundsBuySpecialTargetEntityIds,
			battlegroundsPlayerRatings = battlegroundsPlayerRatings,
			battlegroundsPlayerRaceCounts = battlegroundsPlayerRaceCounts,
			hasBattlegroundsEndGameScreen = flag11,
			hasConstructedEndGameScreen = flag12,
			friendlyPlayState = valueOrDefault,
			opposingPlayState = valueOrDefault2,
			constructedResult = constructedResult,
			endGameScreen = (((Object)(object)val6 == (Object)null) ? null : new
			{
				instanceId = ((Object)val6).GetInstanceID(),
				name = ((Object)val6).name,
				path = GetPath(val6)
			}),
			constructedEndGameScreen = (((Object)(object)val7 == (Object)null) ? null : new
			{
				instanceId = ((Object)val7).GetInstanceID(),
				name = ((Object)val7).name,
				path = GetPath(val7)
			}),
			trinketConfirmButton = (((Object)(object)val8 == (Object)null) ? null : new
			{
				instanceId = ((Object)val8).GetInstanceID(),
				name = ((Object)val8).name,
				path = GetPath(val8)
			}),
			lockOverlayPaths = list2.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
			lockedHeroEntityIds = hashSet.ToArray(),
			objects = list.ToArray(),
			choiceCards = array4,
			friendlyTargetChoiceEntityIds = array6,
			friendlyKerriganChoiceEntityIds = array5,
			constructedOptionsPacketId = constructedOptionsPacketId,
			constructedOptions = constructedOptions
		};
		void MeasurePhase(string key)
		{
			if (phaseTimes != null)
			{
				long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
				phaseTimes[key] = elapsedMilliseconds - phaseStartedMs;
				phaseStartedMs = elapsedMilliseconds;
			}
		}
	}


	private static int[] ReadBattlegroundsAvailableRaces(object gameState)
	{
		try
		{
			if (!(InvokeNoArg(gameState, "GetAvailableRacesInBattlegroundsExcludingAmalgam") is IEnumerable source))
			{
				return Array.Empty<int>();
			}
			return (from race in (from object race in source
					where race != null
					select race).Select((object race) =>
				{
					try
					{
						return Convert.ToInt32(race, CultureInfo.InvariantCulture);
					}
					catch
					{
						return 0;
					}
				})
				where race > 0
				select race).Distinct().ToArray();
		}
		catch
		{
			return Array.Empty<int>();
		}
	}


	private static object[] ReadBattlegroundsOptionTargets(IEnumerable<object> options, object gameState, int friendlyPlayerId)
	{
		List<object> list = new List<object>();
		foreach (object option in options)
		{
			object obj = InvokeNoArg(option, "get_Main");
			int num = ReadIntMember(obj, "get_ID", 0);
			if (obj == null || num <= 0 || !IsPlayErrorInfoValid(InvokeNoArg(obj, "get_PlayErrorInfo")))
			{
				continue;
			}
			object obj2 = ResolveOptionMainEntity(gameState, obj, num);
			string text = ResolveOptionMainCardId(obj, obj2);
			string text2 = InvokeNoArg(obj2, "GetZone")?.ToString() ?? "";
			bool flag = TryInvokeBool(obj2, "IsHeroPower") || string.Equals(InvokeNoArg(obj2, "GetCardType")?.ToString(), "HERO_POWER", StringComparison.OrdinalIgnoreCase);
			bool flag2 = TryInvokeBool(obj, "IsInteractableObject");
			bool flag3 = IsBattlegroundsNativeControlCardId(text);
			bool num2 = ((obj2 == null) ? flag3 : (friendlyPlayerId <= 0 || ReadIntMember(obj2, "GetControllerId", 0) == friendlyPlayerId));
			bool flag4 = (flag3 | flag) || (string.Equals(text2, "HAND", StringComparison.OrdinalIgnoreCase) && (TryInvokeBool(obj2, "IsSpell") || TryInvokeBool(obj2, "IsMinion"))) || ((string.Equals(text2, "PLAY", StringComparison.OrdinalIgnoreCase) & flag2) && (TryInvokeBool(obj2, "IsSpell") || TryInvokeBool(obj2, "IsMinion")));
			if (!num2 || !flag4)
			{
				continue;
			}
			object obj3 = obj;
			int subOptionIndex = -1;
			int num3 = 0;
			string subOptionCardId = "";
			int subOptionPosition = 0;
			if (InvokeNoArg(option, "get_Subs") is IEnumerable enumerable)
			{
				int num4 = 0;
				foreach (object item in enumerable)
				{
					int num5 = num4++;
					if (item != null && IsPlayErrorInfoValid(InvokeNoArg(item, "get_PlayErrorInfo")))
					{
						subOptionIndex = num5;
						num3 = ReadIntMember(item, "get_ID", 0);
						object obj4 = ResolveOptionMainEntity(gameState, item, num3);
						subOptionCardId = ResolveOptionMainCardId(item, obj4);
						subOptionPosition = ReadIntMember(obj4, "GetZonePosition", 0);
						obj3 = item;
						break;
					}
				}
			}
			int[] array = ReadValidOptionTargetEntityIds(obj3);
			bool requiresTarget = array.Length != 0 || TryInvokeBool(obj3, "HasValidTarget");
			list.Add(new
			{
				sourceEntityId = num,
				sourceCardId = text,
				sourceZone = text2,
				isHeroPower = flag,
				isInteractableObject = flag2,
				requiresTarget = requiresTarget,
				validTargetEntityIds = array,
				subOptionIndex = subOptionIndex,
				subOptionEntityId = num3,
				subOptionCardId = subOptionCardId,
				subOptionPosition = subOptionPosition
			});
		}
		return list.ToArray();
	}


	private static bool IsBattlegroundsNativeControlCardId(string cardId)
	{
		if (!cardId.Equals("TB_BaconShop_8p_Reroll_Button", StringComparison.OrdinalIgnoreCase) && !cardId.Equals("TB_BaconShopLockAll_Button", StringComparison.OrdinalIgnoreCase) && !cardId.StartsWith("TB_BaconShopTechUp", StringComparison.OrdinalIgnoreCase) && !cardId.Equals("TB_BaconShop_DragSell", StringComparison.OrdinalIgnoreCase) && !cardId.StartsWith("TB_BaconShop_DragBuy", StringComparison.OrdinalIgnoreCase))
		{
			return cardId.Equals("BG36_Button_DarkGift", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}


	private static int[] ReadBattlegroundsBuyTargetEntityIds(IEnumerable<object> options, object gameState, string exactMainCardId = null)
	{
		HashSet<int> hashSet = new HashSet<int>();
		foreach (object option in options)
		{
			object obj = InvokeNoArg(option, "get_Main");
			if (obj == null)
			{
				continue;
			}
			int mainEntityId = ReadIntMember(obj, "get_ID", 0);
			string text = ResolveOptionMainCardId(gameState, obj, mainEntityId);
			if (!((exactMainCardId == null) ? (!text.StartsWith("TB_BaconShop_DragBuy", StringComparison.OrdinalIgnoreCase)) : (!text.Equals(exactMainCardId, StringComparison.OrdinalIgnoreCase))))
			{
				int[] array = ReadValidOptionTargetEntityIds(obj);
				foreach (int item in array)
				{
					hashSet.Add(item);
				}
			}
		}
		return hashSet.OrderBy((int entityId) => entityId).ToArray();
	}


	private static object ReadBattlegroundsDarkGiftOptionCard(IEnumerable<object> options, object gameState, int friendlyPlayerId)
	{
		foreach (object option in options)
		{
			object obj = InvokeNoArg(option, "get_Main");
			int num = ReadIntMember(obj, "get_ID", 0);
			if (obj == null || num <= 0)
			{
				continue;
			}
			object obj2 = ResolveOptionMainEntity(gameState, obj, num);
			string text = ResolveOptionMainCardId(obj, obj2);
			if (obj2 == null || !string.Equals(text, "BG36_Button_DarkGift", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			int num2 = ReadIntMember(obj2, "GetControllerId", friendlyPlayerId);
			if (friendlyPlayerId <= 0 || num2 <= 0 || num2 == friendlyPlayerId)
			{
				string text2 = InvokeNoArg(obj2, "GetName")?.ToString() ?? "黑暗之赐";
				object obj3 = InvokeNoArg(obj2, "GetZone");
				string text3 = obj3?.ToString() ?? "PLAY";
				int num3 = ReadIntMember(obj2, "GetZonePosition", 0);
				bool? hasResponse = TryInvokeEntityNullableBoolArgsBool(gameState, "HasResponse", obj2, false, null);
				bool? isValidOption = TryInvokeEntityNullableBoolArgsBool(gameState, "IsValidOption", obj2, false, null);
				int num4 = ReadIntMember(obj2, "GetRealTimeCost", 0);
				if (num4 <= 0)
				{
					num4 = ReadEntityTagInt(obj2, "COST") ?? ReadEntityTagInt(obj2, "LAST_KNOWN_COST_IN_HAND").GetValueOrDefault();
				}
				string name = $"{text2} [id={num} cardId={text} zone={text3} zonePos={num3} player={num2}]";
				return new
				{
					instanceId = 0,
					name = name,
					path = "",
					activeInHierarchy = true,
					entity = new
					{
						type = obj2.GetType().FullName,
						id = num,
						cardId = text,
						name = text2,
						zone = obj3,
						zonePosition = num3,
						playerId = num2,
						realTimeCost = num4,
						isSpell = false,
						isBaconSpell = false,
						isMinion = false,
						hasResponse = hasResponse,
						isValidOption = isValidOption,
						playError = InvokeEntityArg(gameState, "GetErrorType", obj2)?.ToString(),
						playErrorParam = InvokeEntityArg(gameState, "GetErrorParam", obj2)?.ToString()
					}
				};
			}
		}
		return null;
	}


	private static int[] ReadValidOptionTargetEntityIds(object optionPart)
	{
		if (!(InvokeNoArg(optionPart, "get_Targets") is IEnumerable source))
		{
			return Array.Empty<int>();
		}
		return (from object target in source
			where target != null
			where IsPlayErrorInfoValid(InvokeNoArg(target, "get_PlayErrorInfo"))
			select ReadIntMember(target, "get_ID", 0) into entityId
			where entityId > 0
			where TryInvokeIntArgBool(optionPart, "IsValidTarget", entityId)
			select entityId).Distinct().ToArray();
	}


	private static object TryInvokeNoArgValue(object instance, string methodName)
	{
		try
		{
			return InvokeNoArg(instance, methodName);
		}
		catch
		{
			return null;
		}
	}


	private static bool IsComponentActive(Component component)
	{
		if ((Object)(object)component != (Object)null && TryGetGameObject(component, out var gameObject))
		{
			return gameObject.activeInHierarchy;
		}
		return false;
	}


	private static GameObject ReadGameObjectMember(object instance, string name)
	{
		object obj = ReadMember(instance, name);
		GameObject val = (GameObject)((obj is GameObject) ? obj : null);
		if (val == null)
		{
			Component val2 = (Component)((obj is Component) ? obj : null);
			if (val2 != null)
			{
				return val2.gameObject;
			}
			return null;
		}
		return val;
	}


	private static int ReadIntValue(object value, int fallback)
	{
		if (value == null || !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return fallback;
		}
		return result;
	}


	private static int ReadBattlegroundsRating()
	{
		try
		{
			return ReadIntMember(InvokeStaticNoArg("BaconLobbyMgr", "Get"), "GetBattlegroundsActiveGameModeRating", -1);
		}
		catch
		{
			return -1;
		}
	}


	private object BuildBattlegroundsEndGameState()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		Component val = ProbeVisibleBattlegroundsEndGameScreen(force: true);
		stopwatch.Stop();
		object gameState = GetGameState();
		int placement = ReadBattlegroundsPlacement(InvokeNoArg(gameState, "GetFriendlySidePlayer") ?? InvokeNoArg(gameState, "GetLocalSidePlayer"), val);
		if ((Object)(object)val == (Object)null || !TryGetGameObject(val, out var gameObject))
		{
			return new
			{
				visible = false,
				placement = placement,
				elapsedMs = stopwatch.ElapsedMilliseconds,
				frame = Time.frameCount,
				instanceId = 0,
				name = "",
				path = ""
			};
		}
		return new
		{
			visible = true,
			placement = placement,
			elapsedMs = stopwatch.ElapsedMilliseconds,
			frame = Time.frameCount,
			instanceId = ((Object)gameObject).GetInstanceID(),
			name = (((Object)gameObject).name ?? ""),
			path = GetPath(gameObject)
		};
	}


	private BridgeResponse ContinueBattlegroundsEndGame(BridgeRequest request)
	{
		Component val = ProbeVisibleBattlegroundsEndGameScreen(force: true);
		if ((Object)(object)val == (Object)null || !TryGetGameObject(val, out var gameObject))
		{
			return BridgeResponse.Success(new
			{
				visible = false,
				invoked = false,
				method = ""
			});
		}
		if (TryContinueEndGameHitbox(val, out var response))
		{
			return response;
		}
		if (TryInvokeZeroArgMethodWithResult(val, "ContinueEvents", out var result, out var methodName, out var error))
		{
			bool flag = GetBool(request.ArgumentsJson, "forceModeReturn", defaultValue: false);
			bool flag2 = default;
			int num;
			if (result is bool)
			{
				flag2 = (bool)result;
				num = 1;
			}
			else
			{
				num = 0;
			}
			bool flag3 = (byte)((uint)num & (flag2 ? 1u : 0u)) != 0;
			bool forcedModeReturn = false;
			List<string> list = new List<string>();
			List<string> list2 = new List<string>();
			if ((flag & flag3) && TryGetGameObject(val, out var gameObject2) && gameObject2.activeInHierarchy)
			{
				if (TryInvokeZeroArgMethod(val, "ContinueButtonPress_Common", out var methodName2, out var error2))
				{
					list.Add(methodName2);
				}
				else if (!string.IsNullOrWhiteSpace(error2))
				{
					list2.Add(error2);
				}
				if (TryInvokeZeroArgMethod(val, "ReturnToPreviousMode", out var methodName3, out var error3))
				{
					list.Add(methodName3);
					forcedModeReturn = true;
				}
				else if (!string.IsNullOrWhiteSpace(error3))
				{
					list2.Add(error3);
				}
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				invoked = true,
				target = Describe(gameObject, includeComponents: false),
				method = methodName,
				error = "",
				continueHandled = flag3,
				forcedModeReturn = forcedModeReturn,
				forcedMethods = list.ToArray(),
				forceError = string.Join(" | ", list2.ToArray())
			});
		}
		string text = TryInvokeCommonClickMethod(gameObject);
		SendCommonClickMessages(gameObject);
		return BridgeResponse.Success(new
		{
			visible = true,
			invoked = !string.IsNullOrWhiteSpace(text),
			target = Describe(gameObject, includeComponents: false),
			method = (text ?? "SendMessage click sequence"),
			error = (error ?? "")
		});
	}


	private static BridgeResponse ContinueConstructedEndGame()
	{
		object obj = InvokeStaticNoArg("EndGameScreen", "Get");
		Component val = (Component)((obj is Component) ? obj : null);
		if (!IsComponentActive(val) || ComponentTypeMatches(val, "BaconEndGameScreen"))
		{
			return BridgeResponse.Success(new
			{
				visible = false,
				invoked = false
			});
		}
		object gameState = GetGameState();
		int valueOrDefault = ReadEntityTagInt(InvokeNoArg(gameState, "GetFriendlySidePlayer") ?? InvokeNoArg(gameState, "GetLocalSidePlayer"), "PLAYSTATE").GetValueOrDefault();
		bool flag = !TryInvokeBool(gameState, "IsGameOver");
		if (flag)
		{
			bool flag2 = (((uint)(valueOrDefault - 4) <= 2u || valueOrDefault == 8) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			return BridgeResponse.Success(new
			{
				visible = false,
				invoked = false
			});
		}
		if (!TryContinueEndGameHitbox(val, out var response))
		{
			return BridgeResponse.Failure("The current end-game screen has no native continue hitbox.");
		}
		return response;
	}


	private static bool TryContinueEndGameHitbox(Component screen, out BridgeResponse response)
	{
		Component val = FindVisibleRewardScrollComponent();
		if ((Object)(object)val != (Object)null)
		{
			RewardPopupDismissResult rewardPopupDismissResult = DismissRewardScrollComponent(val, requireInitialized: true);
			response = BridgeResponse.Success(new
			{
				visible = true,
				invoked = rewardPopupDismissResult.Dismissed,
				rewardPopupVisible = true,
				kind = rewardPopupDismissResult.Kind,
				method = rewardPopupDismissResult.Method,
				error = rewardPopupDismissResult.Error
			});
			return true;
		}
		response = null;
		object obj = ReadMember(screen, "m_hitbox");
		Component val2 = (Component)((obj is Component) ? obj : null);
		if ((Object)(object)val2 == (Object)null)
		{
			return false;
		}
		Type type = FindLoadedType("EndGameScreen");
		int num;
		if (type != null)
		{
			object obj2 = GetCachedInstanceField(type, "m_shown")?.GetValue(screen);
			num = ((obj2 is bool && (bool)obj2) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		int num2;
		if (type != null)
		{
			object obj2 = GetCachedInstanceField(type, "m_hasAlreadySetMode")?.GetValue(screen);
			num2 = ((obj2 is bool && (bool)obj2) ? 1 : 0);
		}
		else
		{
			num2 = 0;
		}
		bool flag = (byte)num2 != 0;
		bool inputBlocked = type != null && GetCachedInstanceField(type, "m_inputBlocker")?.GetValue(screen) is int num3 && num3 > 0;
		if (((num == 0) | flag) || !IsComponentActive(val2))
		{
			response = BridgeResponse.Success(new
			{
				visible = true,
				invoked = false,
				inputBlocked = inputBlocked,
				reason = (flag ? "Returning to previous mode" : "Waiting for native continue hitbox")
			});
			return true;
		}
		bool flag2 = TryInvokeZeroArgMethod(val2, "TriggerRelease", out var methodName, out var error);
		response = (flag2 ? BridgeResponse.Success(new
		{
			visible = true,
			invoked = true,
			inputBlocked = inputBlocked,
			method = "EndGameScreen.m_hitbox." + methodName,
			target = Describe(val2.gameObject, includeComponents: false)
		}) : BridgeResponse.Failure(error));
		return true;
	}


	private object BuildMatchmakingState()
	{
		MatchmakingUiState matchmakingUiState = ReadMatchmakingUiState();
		if ((Object)(object)matchmakingUiState.Popup == (Object)null)
		{
			_matchmakingPopupInstanceId = 0;
			_matchmakingObservedSinceUtc = DateTimeOffset.MinValue;
			return new
			{
				isMatching = false,
				elapsedSeconds = 0.0,
				popupInstanceId = 0,
				cancelButtonInstanceId = 0,
				canCancel = false,
				source = "No active MatchingPopup3D"
			};
		}
		int instanceID = ((Object)matchmakingUiState.Popup).GetInstanceID();
		DateTimeOffset utcNow = DateTimeOffset.UtcNow;
		if (_matchmakingPopupInstanceId != instanceID || _matchmakingObservedSinceUtc == DateTimeOffset.MinValue)
		{
			_matchmakingPopupInstanceId = instanceID;
			_matchmakingObservedSinceUtc = utcNow;
		}
		return new
		{
			isMatching = true,
			elapsedSeconds = Math.Max(0.0, (utcNow - _matchmakingObservedSinceUtc).TotalSeconds),
			matchingObservedSinceUtc = _matchmakingObservedSinceUtc.ToString("O"),
			popupInstanceId = instanceID,
			cancelButtonInstanceId = ((!((Object)(object)matchmakingUiState.CancelButton == (Object)null)) ? ((Object)matchmakingUiState.CancelButton).GetInstanceID() : 0),
			canCancel = ((Object)(object)matchmakingUiState.CancelButton != (Object)null),
			source = "Visible MatchingPopup3D",
			popup = Describe(matchmakingUiState.Popup, includeComponents: true),
			cancelButton = (((Object)(object)matchmakingUiState.CancelButton == (Object)null) ? null : Describe(matchmakingUiState.CancelButton, includeComponents: true)),
			cancelCandidates = matchmakingUiState.CancelCandidates.Select((GameObject item) => Describe(item, includeComponents: true)).ToArray()
		};
	}


	private static object BuildReconnectState()
	{
		object obj = InvokeStaticNoArg("Box", "Get");
		Component val = (Component)((obj is Component) ? obj : null);
		if ((Object)(object)val == (Object)null)
		{
			try
			{
				GameObject val2 = GameObject.Find("TheBox(Clone)") ?? GameObject.Find("/TheBox(Clone)");
				val = ((val2 != null) ? (from component in val2.GetComponents<Component>()
					where (Object)(object)component != (Object)null
					select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, "Box", StringComparison.Ordinal)) : null);
			}
			catch
			{
			}
		}
		string text = ReadComponentState(val);
		object obj3 = InvokeStaticNoArg("Network", "IsLoggedIn");
		if ((Object)(object)val != (Object)null && !IsStartScreenBoxState(text) && obj3 is bool flag && !flag)
		{
			TryGetGameObject(val, out var gameObject);
			return new
			{
				visible = true,
				instanceId = ((!((Object)(object)gameObject == (Object)null)) ? ((Object)gameObject).GetInstanceID() : 0),
				state = text,
				networkLoggedIn = flag,
				scanSource = "Network.IsLoggedIn()"
			};
		}
		if ((Object)(object)val != (Object)null && ContainsAny(text, "ERROR"))
		{
			TryGetGameObject(val, out var gameObject2);
			return new
			{
				visible = true,
				instanceId = ((!((Object)(object)gameObject2 == (Object)null)) ? ((Object)gameObject2).GetInstanceID() : 0),
				state = text,
				networkLoggedIn = ((obj3 is bool value) ? new bool?(value) : ((bool?)null)),
				scanSource = "Box.Get().GetState()"
			};
		}
		Type type = FindLoadedComponentType("ReconnectHelperDialog");
		Component val3 = ((type == null) ? null : Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault((Component component) => TryGetGameObject(component, out var gameObject4) && gameObject4.activeInHierarchy));
		if ((Object)(object)val3 == (Object)null || !TryGetGameObject(val3, out var gameObject3))
		{
			return new
			{
				visible = false,
				instanceId = 0,
				state = "",
				networkLoggedIn = ((obj3 is bool value2) ? new bool?(value2) : ((bool?)null)),
				scanSource = "typed ReconnectHelperDialog"
			};
		}
		return new
		{
			visible = true,
			instanceId = ((Object)gameObject3).GetInstanceID(),
			state = (ReadMemberAsString(val3, "m_state") ?? ""),
			scanSource = "typed ReconnectHelperDialog"
		};
	}


	private static BridgeResponse AttemptReconnect()
	{
		Type type = FindLoadedComponentType("ReconnectHelperDialog");
		Component val = ((type == null) ? null : Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault((Component component) => TryGetGameObject(component, out var gameObject2) && gameObject2.activeInHierarchy));
		if ((Object)(object)val == (Object)null || !TryGetGameObject(val, out var gameObject))
		{
			return BridgeResponse.Success(new
			{
				visible = false,
				attempted = false,
				state = "",
				method = ""
			});
		}
		string state = ReadMemberAsString(val, "m_state") ?? "";
		MethodInfo method = ((object)val).GetType().GetMethod("OnReconnectButtonPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
		if (method == null)
		{
			return BridgeResponse.Success(new
			{
				visible = true,
				attempted = false,
				state = state,
				target = Describe(gameObject, includeComponents: false),
				method = "",
				error = "ReconnectHelperDialog.OnReconnectButtonPressed was not available."
			});
		}
		try
		{
			method.Invoke(val, null);
			return BridgeResponse.Success(new
			{
				visible = true,
				attempted = true,
				state = state,
				target = Describe(gameObject, includeComponents: false),
				method = "ReconnectHelperDialog.OnReconnectButtonPressed",
				error = ""
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure("ReconnectHelperDialog.OnReconnectButtonPressed failed: " + ex.GetBaseException().Message);
		}
	}


	private BridgeResponse CancelMatchmaking(BridgeRequest request)
	{
		MatchmakingUiState matchmakingUiState = ReadMatchmakingUiState();
		if ((Object)(object)matchmakingUiState.Popup == (Object)null)
		{
			return BridgeResponse.Failure("No active MatchingPopup3D is visible; cancellation was not attempted.");
		}
		int num = GetInt(request.ArgumentsJson, "expectedPopupInstanceId", 0);
		if (num != 0 && ((Object)matchmakingUiState.Popup).GetInstanceID() != num)
		{
			return BridgeResponse.Failure("The active MatchingPopup3D changed before cancellation; cancellation was not attempted.");
		}
		if ((Object)(object)matchmakingUiState.CancelButton == (Object)null)
		{
			return BridgeResponse.Failure("The active MatchingPopup3D has no verified cancel button.");
		}
		int num2 = GetInt(request.ArgumentsJson, "expectedCancelButtonInstanceId", 0);
		if (num2 != 0 && ((Object)matchmakingUiState.CancelButton).GetInstanceID() != num2)
		{
			return BridgeResponse.Failure("The active matchmaking cancel button changed before cancellation; cancellation was not attempted.");
		}
		Button component = matchmakingUiState.CancelButton.GetComponent<Button>();
		string text;
		if ((Object)(object)component != (Object)null)
		{
			((UnityEvent)component.onClick).Invoke();
			text = "UnityEngine.UI.Button.onClick";
		}
		else
		{
			text = TryInvokePegUiPressRelease(matchmakingUiState.CancelButton) ?? TryInvokePegUiClick(matchmakingUiState.CancelButton) ?? TryInvokeCommonClickMethod(matchmakingUiState.CancelButton);
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return BridgeResponse.Failure("The verified matchmaking cancel button exposes no safe internal click method.");
		}
		return BridgeResponse.Success(new
		{
			accepted = true,
			popupInstanceId = ((Object)matchmakingUiState.Popup).GetInstanceID(),
			cancelButtonInstanceId = ((Object)matchmakingUiState.CancelButton).GetInstanceID(),
			popup = Describe(matchmakingUiState.Popup, includeComponents: false),
			cancelButton = Describe(matchmakingUiState.CancelButton, includeComponents: true),
			method = text
		});
	}


	private static MatchmakingUiState ReadMatchmakingUiState()
	{
		GameObject val = (from item in (from item in EnumerateObjects()
				where item.activeInHierarchy
				select item).ToArray().Where(IsAuthoritativeMatchmakingPopup)
			orderby GetPath(item).Length
			select item).ThenBy(GetPath, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
		if ((Object)(object)val == (Object)null)
		{
			return new MatchmakingUiState();
		}
		GameObject[] array = (from item in (from item in val.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null && ((Component)item).gameObject.activeInHierarchy
				select ((Component)item).gameObject).Where(IsVerifiedMatchmakingCancelButton)
			group item by ((Object)item).GetInstanceID() into @group
			select @group.First()).OrderBy(MatchmakingCancelButtonPriority).ThenBy((GameObject item) => GetPath(item).Length).ToArray();
		return new MatchmakingUiState
		{
			Popup = val,
			CancelButton = array.FirstOrDefault(),
			CancelCandidates = array
		};
	}


	private static bool IsAuthoritativeMatchmakingPopup(GameObject gameObject)
	{
		if ((((Object)gameObject).name + " " + GetPath(gameObject)).IndexOf("MatchingPopup3D", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return false;
		}
		if (((Object)gameObject).name.IndexOf("MatchingPopup3D", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return (from item in gameObject.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).Any((Component item) => ((object)item).GetType().Name.IndexOf("MatchingPopup", StringComparison.OrdinalIgnoreCase) >= 0);
		}
		return true;
	}


	private static bool IsVerifiedMatchmakingCancelButton(GameObject gameObject)
	{
		if (!ContainsAny(((Object)gameObject).name + " " + GetPath(gameObject), "CancelButton", "Button_Cancel", "ButtonCancel", "/Cancel") && !HasDescendantText(gameObject, "GLOBAL_CANCEL", "取消匹配", "取消"))
		{
			return false;
		}
		if ((Object)(object)gameObject.GetComponent<Button>() != (Object)null)
		{
			return true;
		}
		return (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ContainsAny(((object)item).GetType().Name, "PegUIElement", "UIBButton", "NormalButton", "Clickable", "Button"));
	}


	private static int MatchmakingCancelButtonPriority(GameObject gameObject)
	{
		string a = ((Object)gameObject).name ?? "";
		if (string.Equals(a, "CancelButton", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Button_Cancel", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (!HasDescendantText(gameObject, "GLOBAL_CANCEL", "取消匹配", "取消"))
		{
			return 2;
		}
		return 1;
	}


	private static object[] ReadConstructedOptions()
	{
		object gameState = GetGameState();
		if (!(InvokeNoArg(InvokeNoArg(gameState, "GetOptionsPacket"), "get_List") is IEnumerable enumerable))
		{
			return Array.Empty<object>();
		}
		Dictionary<int, (string, int, int)> dictionary = new Dictionary<int, (string, int, int)>();
		if (InvokeNoArg(InvokeNoArg(gameState, "GetEntityMap"), "get_Values") is IEnumerable enumerable2)
		{
			foreach (object item in enumerable2)
			{
				int num = ReadIntMember(item, "GetEntityId", 0);
				if (num > 0)
				{
					dictionary[num] = (InvokeNoArg(item, "GetCardId")?.ToString() ?? "", ReadIntMember(item, "GetZonePosition", 0), ReadIntMember(item, "GetControllerId", 0));
				}
			}
		}
		List<object> list = new List<object>();
		int num2 = 0;
		foreach (object item2 in enumerable)
		{
			object instance = InvokeNoArg(item2, "get_Main");
			int num3 = ReadIntMember(instance, "get_ID", -1);
			List<object> list2 = new List<object>();
			if (InvokeNoArg(instance, "get_Targets") is IEnumerable enumerable3)
			{
				foreach (object item3 in enumerable3)
				{
					int num4 = ReadIntMember(item3, "get_ID", -1);
					if (num4 > 0)
					{
						object playErrorInfo = InvokeNoArg(item3, "get_PlayErrorInfo");
						list2.Add(new
						{
							entityId = num4,
							valid = IsPlayErrorInfoValid(playErrorInfo),
							playError = DescribePlayErrorInfo(playErrorInfo)
						});
					}
				}
			}
			List<object> list3 = new List<object>();
			int num5 = 0;
			if (InvokeNoArg(item2, "get_Subs") is IEnumerable enumerable4)
			{
				foreach (object item4 in enumerable4)
				{
					int num6 = ReadIntMember(item4, "get_ID", -1);
					List<object> list4 = new List<object>();
					if (InvokeNoArg(item4, "get_Targets") is IEnumerable enumerable5)
					{
						foreach (object item5 in enumerable5)
						{
							int num7 = ReadIntMember(item5, "get_ID", -1);
							if (num7 > 0)
							{
								object playErrorInfo2 = InvokeNoArg(item5, "get_PlayErrorInfo");
								list4.Add(new
								{
									entityId = num7,
									valid = IsPlayErrorInfoValid(playErrorInfo2),
									playError = DescribePlayErrorInfo(playErrorInfo2)
								});
							}
						}
					}
					dictionary.TryGetValue(num6, out var value);
					list3.Add(new
					{
						index = num5++,
						entityId = num6,
						cardId = value.Item1,
						zonePosition = value.Item2,
						powerKeyword = (InvokeNoArg(item4, "get_PowerKeyword")?.ToString() ?? ""),
						playError = DescribePlayErrorInfo(InvokeNoArg(item4, "get_PlayErrorInfo")),
						targets = list4.ToArray()
					});
				}
			}
			list.Add(new
			{
				index = num2,
				type = ReadIntMember(item2, "get_Type", int.MinValue),
				typeName = (InvokeNoArg(item2, "get_Type")?.ToString() ?? ""),
				mainEntityId = num3,
				mainPlayerId = (dictionary.TryGetValue(num3, out var value2) ? value2.Item3 : 0),
				isTrade = (TryInvokeBool(item2, "IsTrade") || TryInvokeBool(item2, "get_IsTrade") || TryInvokeBool(instance, "IsTrade") || TryInvokeBool(instance, "get_IsTrade")),
				powerKeyword = (InvokeNoArg(instance, "get_PowerKeyword")?.ToString() ?? ""),
				hasValidTarget = TryInvokeBool(instance, "HasValidTarget"),
				playError = DescribePlayErrorInfo(InvokeNoArg(instance, "get_PlayErrorInfo")),
				targets = list2.ToArray(),
				subOptions = list3.ToArray()
			});
			num2++;
		}
		return list.ToArray();
	}


	private static int ReadConstructedOptionsPacketId()
	{
		return ReadIntMember(InvokeNoArg(GetGameState(), "GetOptionsPacket"), "get_ID", 0);
	}


	private static object BuildConstructedLobbyState()
	{
		GameObject val = GameObject.Find("/Tournament(Clone)/DeckPickerTray(Clone)") ?? GameObject.Find("Tournament(Clone)/DeckPickerTray(Clone)");
		return new
		{
			visible = ((Object)(object)val != (Object)null && val.activeInHierarchy)
		};
	}


	private static object InvokeInt64Arg(object instance, string methodName, long value)
	{
		return instance?.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, methodName, StringComparison.Ordinal) && item.GetParameters().Length == 1 && item.GetParameters()[0].ParameterType == typeof(long))?.Invoke(instance, new object[1] { value });
	}


	private object BuildAccountDetails(BridgeRequest request)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		bool flag = IsNetworkLoggedIn();
		string playerName = ReadLocalPlayerName(flag);
		bool flag2 = IsActiveGameInProgress();
		if (flag2 && !GetBool(request.ArgumentsJson, "allowInGame", defaultValue: true))
		{
			stopwatch.Stop();
			return new
			{
				ready = false,
				loggedIn = flag,
				playerName = playerName,
				identityAvailable = playerName.Length > 0,
				rewardTracksReady = false,
				goldAvailable = false,
				dustAvailable = false,
				arenaTicketsAvailable = false,
				battlegroundsRatingAvailable = false,
				rankedPlayAvailable = false,
				inGame = flag2,
				reason = "Account details automatic refresh is deferred during an active game.",
				elapsedMs = stopwatch.ElapsedMilliseconds,
				time = DateTimeOffset.Now.ToString("O"),
				rewardTrack = (object)null,
				gold = 0L,
				spendableDust = 0L,
				massDisenchantDust = 0L,
				dust = 0L,
				arenaTickets = 0L,
				battlegroundsRating = -1,
				constructedRanks = (object)null,
				questStatus = (object)null
			};
		}
		object rewardTrackManager = GetRewardTrackManager();
		bool flag3 = rewardTrackManager != null && ReadMemberAsBool(rewardTrackManager, "HasReceivedRewardTracksFromServer", defaultValue: false);
		object instance = null;
		object obj = null;
		int num = 0;
		int num2 = int.MaxValue;
		if (flag3)
		{
			foreach (object item in EnumerateRewardTracks(rewardTrackManager))
			{
				if (item == null)
				{
					continue;
				}
				TryRefreshRewardTrack(item);
				object obj2 = InvokeNoArg(item, "get_TrackDataModel");
				if (obj2 == null || ReadMemberAsBool(obj2, "Expired", defaultValue: false))
				{
					continue;
				}
				int num3 = ReadIntMember(item, "get_RewardTrackId", ReadMemberAsInt(obj2, "RewardTrackId", 0));
				if (num3 <= 0)
				{
					continue;
				}
				int accountDetailsRewardTrackPriority = GetAccountDetailsRewardTrackPriority(ReadIntMember(obj2, "get_RewardTrackType", 0));
				if (accountDetailsRewardTrackPriority < num2)
				{
					instance = item;
					obj = obj2;
					num = num3;
					num2 = accountDetailsRewardTrackPriority;
					if (num2 == 0)
					{
						break;
					}
				}
			}
		}
		int num4 = ((num > 0) ? num : ReadIntMember(instance, "get_RewardTrackId", ReadMemberAsInt(obj, "RewardTrackId", 0)));
		int currentLevel = Math.Max(0, ReadMemberAsInt(obj, "Level", 0));
		int levelSoftCap = Math.Max(0, ReadMemberAsInt(obj, "LevelSoftCap", 0));
		int levelHardCap = Math.Max(0, ReadMemberAsInt(obj, "LevelHardCap", 0));
		long num5 = ReadRewardTrackMaximumExperience(num4, ref levelHardCap);
		long totalExperience = Math.Max(0L, ReadMemberAsLong(obj, "TotalXp", 0L));
		long currentLevelExperience = Math.Max(0L, ReadMemberAsLong(obj, "Xp", 0L));
		long nextLevelExperience = Math.Max(0L, ReadMemberAsLong(obj, "XpNeeded", 0L));
		bool flag4 = TryReadGoldBalance(out var gold, out var available, out var reason);
		bool flag5 = TryReadDustBalance(out var spendableDust, out var massDisenchantDust, out var available2, out var reason2);
		bool preferVisibleRankDataModel = !flag2 && GetBool(request.ArgumentsJson, "preferVisibleConstructedRank", defaultValue: false);
		ConstructedRankReadResult constructedRankReadResult = ReadConstructedRank(2, "Standard", preferVisibleRankDataModel);
		ConstructedRankReadResult constructedRankReadResult2 = ReadConstructedRank(1, "Wild", preferVisibleRankDataModel);
		bool flag6 = TryReadCurrencyBalance(9, "Tavern ticket", out var balanceValue, out var available3, out var reason3);
		int num6 = ReadBattlegroundsRating();
		bool battlegroundsRatingAvailable = num6 >= 0;
		bool rankedPlayAvailable = constructedRankReadResult.Available || constructedRankReadResult2.Available;
		object questStatus = BuildQuestStatus(includeTasks: true);
		bool num7 = ((flag & flag3) && obj != null && num4 > 0 && num5 > 0) & flag4 & available;
		string reason4;
		if (num7)
		{
			reason4 = "";
		}
		else if (flag)
		{
			if (flag3)
			{
				if (obj == null)
				{
					reason4 = "No supported account reward track is available yet.";
				}
				else
				{
					reason4 = ((!flag4 || !available) ? reason : "Account details are not ready yet.");
				}
			}
			else
			{
				reason4 = "Reward track state has not been received from the server yet.";
			}
		}
		else
		{
			reason4 = "Network.IsLoggedIn() returned false.";
		}
		stopwatch.Stop();
		return new
		{
			ready = num7,
			loggedIn = flag,
			rewardTracksReady = flag3,
			goldAvailable = available,
			dustAvailable = available2,
			arenaTicketsAvailable = (flag6 & available3),
			battlegroundsRatingAvailable = battlegroundsRatingAvailable,
			rankedPlayAvailable = rankedPlayAvailable,
			inGame = flag2,
			reason = reason4,
			elapsedMs = stopwatch.ElapsedMilliseconds,
			time = DateTimeOffset.Now.ToString("O"),
			rewardTrack = ((obj == null) ? null : new
			{
				trackId = num4,
				name = (ReadMemberAsString(obj, "Name") ?? ""),
				season = Math.Max(0, ReadMemberAsInt(obj, "Season", 0)),
				currentLevel = currentLevel,
				currentLevelExperience = currentLevelExperience,
				nextLevelExperience = nextLevelExperience,
				totalExperience = totalExperience,
				maximumExperience = num5,
				levelSoftCap = levelSoftCap,
				levelHardCap = levelHardCap
			}),
			gold = Math.Max(0L, gold),
			goldReason = (available ? "" : reason),
			spendableDust = Math.Max(0L, spendableDust),
			massDisenchantDust = Math.Max(0L, massDisenchantDust),
			dust = Math.Max(0L, spendableDust) + Math.Max(0L, massDisenchantDust),
			arenaTickets = Math.Max(0L, balanceValue),
			cardPackCount = (flag2 ? ((long?)null) : ReadCardPackCount()),
			undergroundArenaUnlocked = (flag2 ? ((bool?)null) : ReadUndergroundArenaUnlocked()),
			arenaTicketsReason = ((flag6 & available3) ? "" : reason3),
			battlegroundsRating = num6,
			playerName = playerName,
			identityAvailable = playerName.Length > 0,
			dustReason = ((flag5 & available2) ? "" : reason2),
			constructedRanks = new
			{
				standard = constructedRankReadResult,
				wild = constructedRankReadResult2
			},
			questStatus = questStatus
		};
	}

	private static string ReadLocalPlayerName(bool loggedIn)
	{
		return PlayerIdentityReader.ReadName(loggedIn,
			() => InvokeNoArg(InvokeStaticNoArg("BnetPresenceMgr", "Get"), "GetMyPlayer"),
			() => InvokeNoArg(GetGameState(), "GetFriendlySidePlayer") ?? InvokeNoArg(GetGameState(), "GetLocalSidePlayer"));
	}

	private static object BuildPlayerIdentity()
	{
		bool loggedIn = IsNetworkLoggedIn();
		string playerName = ReadLocalPlayerName(loggedIn);
		return new { loggedIn, playerName, identityAvailable = playerName.Length > 0, processId = Process.GetCurrentProcess().Id };
	}


	private static int GetAccountDetailsRewardTrackPriority(int rewardTrackType)
	{
		return rewardTrackType switch
		{
			1 => 0, 
			8 => 1, 
			2 => 2, 
			_ => int.MaxValue, 
		};
	}


	private static long ReadRewardTrackMaximumExperience(int trackId, ref int levelHardCap)
	{
		if (trackId <= 0)
		{
			return 0L;
		}
		object[] records = (InvokeStaticIntArg("GameUtils", "GetRewardTrackLevelsForRewardTrack", trackId) as IEnumerable)?.Cast<object>().Where((object record) => record != null).ToArray() ?? Array.Empty<object>();
		if (records.Length == 0)
		{
			return 0L;
		}
		if (levelHardCap <= 0)
		{
			levelHardCap = records.Select((object record) => ReadIntMember(record, "get_Level", 0)).DefaultIfEmpty(records.Length).Max();
		}
		if (levelHardCap <= 1)
		{
			return 0L;
		}
		int resolvedLevelHardCap = levelHardCap;
		long key = ((long)trackId << 32) | (uint)resolvedLevelHardCap;
		return RewardTrackMaximumExperienceCache.GetOrAdd(key, (long _) =>
		{
			long num = 0L;
			object[] array = records;
			foreach (object instance in array)
			{
				int num2 = ReadIntMember(instance, "get_Level", 0);
				if (num2 > 0 && num2 < resolvedLevelHardCap)
				{
					num += Math.Max(0, ReadIntMember(instance, "get_XpNeeded", 0));
				}
			}
			return num;
		});
	}


	private static bool TryReadGoldBalance(out long gold, out bool available, out string reason)
	{
		return TryReadCurrencyBalance(1, "Gold", out gold, out available, out reason);
	}


	private static bool TryReadDustBalance(out long spendableDust, out long massDisenchantDust, out bool available, out string reason)
	{
		spendableDust = 0L;
		massDisenchantDust = 0L;
		available = false;
		reason = "Dust balance is not available yet.";
		if (!TryReadCurrencyBalance(2, "Dust", out spendableDust, out var available2, out reason) || !available2)
		{
			return false;
		}
		try
		{
			object obj = InvokeStaticNoArg("CollectionManager", "Get");
			if (obj == null)
			{
				reason = "CollectionManager is not available yet.";
				return false;
			}
			object obj2 = InvokeNoArg(obj, "IsFullyLoaded");
			if (!(obj2 is bool) || !(bool)obj2)
			{
				reason = "CollectionManager has not finished loading the collection.";
				return false;
			}
			if (!TryCalculateMassDisenchantDust(obj, out massDisenchantDust, out reason))
			{
				return false;
			}
			available = true;
			reason = "";
			return true;
		}
		catch (Exception ex)
		{
			reason = ((ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException.Message : ex.Message);
			return false;
		}
	}


	private static bool TryCalculateMassDisenchantDust(object collectionManager, out long massDisenchantDust, out string reason)
	{
		massDisenchantDust = 0L;
		reason = "";
		if (!(InvokeNoArg(collectionManager, "GetOwnedCards") is IEnumerable enumerable))
		{
			reason = "CollectionManager.GetOwnedCards() was not found.";
			return false;
		}
		foreach (object item in enumerable)
		{
			if (item == null)
			{
				continue;
			}
			object obj = InvokeNoArg(item, "get_DisenchantCount");
			if (((obj != null && Convert.ToInt32(obj, CultureInfo.InvariantCulture) != 0) ? 1 : 0) <= (false ? 1 : 0))
			{
				continue;
			}
			object obj2 = InvokeNoArg(item, "get_PremiumType");
			int num = ((obj2 != null) ? Convert.ToInt32(obj2, CultureInfo.InvariantCulture) : 0);
			if (num == 3)
			{
				continue;
			}
			string text = InvokeNoArg(item, "get_CardId")?.ToString() ?? "";
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			object obj3 = InvokeStaticStringEnumArg("CraftingManager", "GetCardValue", text, num);
			if (obj3 != null)
			{
				object obj4 = InvokeNoArg(item, "get_IsCraftableDisenchantCount");
				int num2 = ((obj4 != null) ? Convert.ToInt32(obj4, CultureInfo.InvariantCulture) : 0);
				object obj5 = InvokeNoArg(obj3, "GetSellValue");
				int num3 = ((obj5 != null) ? Convert.ToInt32(obj5, CultureInfo.InvariantCulture) : 0);
				if (num2 > 0 && num3 > 0)
				{
					massDisenchantDust += (long)num2 * (long)num3;
				}
			}
		}
		massDisenchantDust = Math.Max(0L, massDisenchantDust);
		return true;
	}


	private static bool TryReadCurrencyBalance(int currencyTypeValue, string currencyLabel, out long balanceValue, out bool available, out string reason)
	{
		balanceValue = 0L;
		available = false;
		reason = currencyLabel + " balance is not available yet.";
		try
		{
			Type type = FindLoadedType("Blizzard.T5.Services.ServiceManager");
			Type type2 = FindLoadedType("CurrencyManager");
			object obj = (type?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo method) => string.Equals(method.Name, "Get", StringComparison.Ordinal) && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 1 && method.GetParameters().Length == 0))?.MakeGenericMethod(type2).Invoke(null, null);
			if (obj == null)
			{
				reason = "CurrencyManager is not available yet.";
				return false;
			}
			return AccountCurrencyReader.TryRead(obj, currencyTypeValue, currencyLabel, DateTime.UtcNow, !IsActiveGameInProgress(), out balanceValue, out available, out reason);
		}
		catch (Exception ex)
		{
			reason = ((ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException.Message : ex.Message);
			return false;
		}
	}


	private static ConstructedRankReadResult ReadConstructedRank(int formatValue, string formatName, bool preferVisibleRankDataModel)
	{
		try
		{
			object obj = InvokeStaticNoArg("RankMgr", "Get");
			if (obj == null)
			{
				return ConstructedRankReadResult.Unavailable(formatName, "RankMgr is not available yet.");
			}
			object obj2 = InvokeNoArg(obj, "get_HasLocalPlayerMedalInfo");
			if (!(obj2 is bool) || !(bool)obj2)
			{
				return ConstructedRankReadResult.Unavailable(formatName, "RankMgr has not received local player medal information yet.");
			}
			object obj3 = InvokeEnumArg(InvokeNoArg(obj, "GetLocalPlayerMedalInfo"), "GetCurrentMedal", formatValue);
			if (obj3 != null)
			{
				obj2 = InvokeNoArg(obj3, "IsValid");
				if (obj2 is bool && (bool)obj2)
				{
					obj2 = InvokeNoArg(obj3, "IsNewPlayer");
					bool flag = default;
					int num;
					if (obj2 is bool)
					{
						flag = (bool)obj2;
						num = 1;
					}
					else
					{
						num = 0;
					}
					bool isNewPlayer = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
					obj2 = InvokeNoArg(obj3, "IsLegendRank");
					bool flag2 = default;
					int num2;
					if (obj2 is bool)
					{
						flag2 = (bool)obj2;
						num2 = 1;
					}
					else
					{
						num2 = 0;
					}
					bool flag3 = (byte)((uint)num2 & (flag2 ? 1u : 0u)) != 0;
					int num3 = Math.Max(0, ReadMemberAsInt(obj3, "legendIndex", 0));
					int starLevel = Math.Max(0, ReadMemberAsInt(obj3, "starLevel", 0));
					int stars = Math.Max(0, ReadMemberAsInt(obj3, "earnedStars", ReadMemberAsInt(obj3, "Stars", ReadMemberAsInt(obj3, "stars", 0))));
					if (preferVisibleRankDataModel && TryReadVisibleConstructedStars(formatValue, out var stars2))
					{
						stars = stars2;
					}
					int leagueId = Math.Max(0, ReadMemberAsInt(obj3, "leagueId", 0));
					string rankName = InvokeNoArg(obj3, "GetRankName")?.ToString() ?? "";
					string medalText = InvokeNoArg(obj3, "GetMedalText")?.ToString() ?? "";
					string cheatName = ReadMemberAsString(ReadMember(obj3, "RankConfig") ?? InvokeNoArg(obj3, "get_RankConfig"), "CheatName") ?? "";
					string displayName;
					if (flag3)
					{
						displayName = ((num3 > 0) ? $"传说 {num3}" : "传说");
					}
					else
					{
						displayName = JoinRankDisplayName(rankName, medalText, cheatName, starLevel);
					}
					return new ConstructedRankReadResult(available: true, formatName, isNewPlayer, flag3, num3, starLevel, stars, leagueId, rankName, medalText, cheatName, displayName, "");
				}
			}
			return ConstructedRankReadResult.Unavailable(formatName, "No valid " + formatName + " medal is available.");
		}
		catch (Exception ex)
		{
			string reason = ((ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException.Message : ex.Message);
			return ConstructedRankReadResult.Unavailable(formatName, reason);
		}
	}


	private static bool TryReadVisibleConstructedStars(int formatValue, out int stars)
	{
		stars = 0;
		try
		{
			Type type = FindLoadedComponentType("RankedPlayDisplay");
			if (type != null)
			{
				foreach (Component item in Object.FindObjectsOfType(type).OfType<Component>())
				{
					if ((Object)(object)item == (Object)null || (Object)(object)item.gameObject == (Object)null || !item.gameObject.activeInHierarchy)
					{
						continue;
					}
					object obj = TryGetDataModel(ReadMember(item, "m_rankContainerVisualController"), 123) ?? ReadMember(item, "m_rankedChestDataModel");
					if (obj != null && ReadIntMember(obj, "get_FormatType", -1) == formatValue)
					{
						int num = ReadIntMember(obj, "get_Stars", -1);
						if (num >= 0)
						{
							stars = num;
							return true;
						}
					}
				}
			}
			Type type2 = FindLoadedComponentType("RankedMedal");
			if (type2 == null)
			{
				return false;
			}
			foreach (Component item2 in Object.FindObjectsOfType(type2).OfType<Component>())
			{
				if ((Object)(object)item2 == (Object)null || (Object)(object)item2.gameObject == (Object)null || !item2.gameObject.activeInHierarchy)
				{
					continue;
				}
				object obj2 = InvokeNoArg(item2, "GetRankedPlayDataModel");
				if (obj2 != null && ReadIntMember(obj2, "get_FormatType", -1) == formatValue)
				{
					int num2 = ReadIntMember(obj2, "get_Stars", -1);
					if (num2 >= 0)
					{
						stars = num2;
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}


	private static object TryGetDataModel(object provider, int modelId)
	{
		if (provider == null)
		{
			return null;
		}
		try
		{
			MethodInfo methodInfo = provider.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, "GetDataModel", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType.IsByRef;
			});
			if (methodInfo == null)
			{
				return null;
			}
			object[] array = new object[2] { modelId, null };
			object obj = methodInfo.Invoke(provider, array);
			bool flag = default;
			int num;
			if (obj is bool)
			{
				flag = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			return (((uint)num & (flag ? 1u : 0u)) != 0) ? array[1] : null;
		}
		catch
		{
			return null;
		}
	}


	private static string JoinRankDisplayName(string rankName, string medalText, string cheatName, int starLevel)
	{
		string text = rankName?.Trim() ?? "";
		string text2 = medalText?.Trim() ?? "";
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2) && text.EndsWith(text2, StringComparison.OrdinalIgnoreCase))
		{
			return text;
		}
		string[] array = new string[2] { text, text2 }.Where((string part) => !string.IsNullOrWhiteSpace(part)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length != 0)
		{
			return string.Join(" ", array);
		}
		if (!string.IsNullOrWhiteSpace(cheatName))
		{
			return cheatName.Trim();
		}
		if (starLevel <= 0)
		{
			return "未知段位";
		}
		return $"星级 {starLevel}";
	}


	private object BuildQuestStatus(bool includeTasks)
	{
		object questManager = GetQuestManager();
		if (questManager == null)
		{
			return new
			{
				ready = false,
				hasReceivedQuestStatesFromServer = false,
				time = DateTimeOffset.Now.ToString("O"),
				tasks = Array.Empty<object>(),
				pools = Array.Empty<object>(),
				error = "Hearthstone.Progression.QuestManager is not available."
			};
		}
		bool flag = ReadMemberAsBool(questManager, "HasReceivedQuestStatesFromServer", defaultValue: false);
		QuestStatusItem[] array = (flag ? ReadActiveQuestDataModels(questManager) : Array.Empty<QuestStatusItem>());
		QuestStatusItem[] tasks = (includeTasks ? array : Array.Empty<QuestStatusItem>());
		Dictionary<int, string> taskPoolTypes = (from task in array
			where task.PoolId > 0 && !string.IsNullOrWhiteSpace(task.PoolType)
			group task by task.PoolId).ToDictionary((IGrouping<int, QuestStatusItem> group) => group.Key, (IGrouping<int, QuestStatusItem> group) => group.First().PoolType);
		QuestPoolStatusItem[] pools = ReadQuestPoolStates(questManager, taskPoolTypes);
		return new
		{
			ready = flag,
			hasReceivedQuestStatesFromServer = flag,
			time = DateTimeOffset.Now.ToString("O"),
			tasks = tasks,
			pools = pools,
			error = (flag ? "" : "Quest states have not been received from the server yet.")
		};
	}


	private static object GetQuestManager()
	{
		try
		{
			return (FindLoadedType("Hearthstone.Progression.QuestManager")?.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null))?.Invoke(null, null);
		}
		catch
		{
			return null;
		}
	}


	private static QuestStatusItem[] ReadActiveQuestDataModels(object manager)
	{
		Dictionary<int, QuestStatusItem> dictionary = new Dictionary<int, QuestStatusItem>();
		int[] array = new int[3] { 0, 1, 2 };
		foreach (int num in array)
		{
			int[] array2 = new int[1] { 1 };
			foreach (int rewardTrackType in array2)
			{
				if (!(ReadMember(CreateActiveQuestListDataModel(manager, num, rewardTrackType), "Quests") is IEnumerable enumerable))
				{
					continue;
				}
				foreach (object item in enumerable)
				{
					if (item != null)
					{
						int num2 = ReadMemberAsInt(item, "QuestId", 0);
						if (num2 > 0)
						{
							dictionary[num2] = new QuestStatusItem
							{
								QuestId = num2,
								PoolId = ReadMemberAsInt(item, "PoolId", 0),
								PoolType = (ReadMemberAsString(item, "PoolType") ?? (num switch
								{
									2 => "WEEKLY", 
									1 => "DAILY", 
									_ => "NONE", 
								})),
								Name = (ReadMemberAsString(item, "Name") ?? ""),
								Description = (ReadMemberAsString(item, "Description") ?? ""),
								Progress = Math.Max(0, ReadMemberAsInt(item, "Progress", 0)),
								Quota = Math.Max(0, ReadMemberAsInt(item, "Quota", 0)),
								RerollCount = Math.Max(0, ReadMemberAsInt(item, "RerollCount", 0)),
								RewardTrackXp = Math.Max(0, ReadMemberAsInt(item, "RewardTrackXp", 0)),
								Status = (ReadMemberAsString(item, "Status") ?? ""),
								IsChainQuest = ReadMemberAsBool(item, "IsChainQuest", defaultValue: false),
								TimeUntilExpiration = (ReadMemberAsString(item, "TimeUntilExpiration") ?? "")
							};
						}
					}
				}
			}
		}
		return (from item in dictionary.Values
			orderby (!string.Equals(item.PoolType, "DAILY", StringComparison.OrdinalIgnoreCase)) ? 1 : 0, item.QuestId descending
			select item).ToArray();
	}


	private static object CreateActiveQuestListDataModel(object manager, int poolType, int rewardTrackType)
	{
		if (manager == null)
		{
			return null;
		}
		try
		{
			MethodInfo methodInfo = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "CreateActiveQuestsDataModel", StringComparison.Ordinal) && item.GetParameters().Length == 3);
			if (methodInfo == null)
			{
				return null;
			}
			ParameterInfo[] parameters = methodInfo.GetParameters();
			return methodInfo.Invoke(manager, new object[3]
			{
				ConvertIntArgument(poolType, parameters[0].ParameterType),
				ConvertIntArgument(rewardTrackType, parameters[1].ParameterType),
				false
			});
		}
		catch
		{
			return null;
		}
	}


	private static QuestPoolStatusItem[] ReadQuestPoolStates(object manager, IReadOnlyDictionary<int, string> taskPoolTypes)
	{
		if (!(ReadMember(manager, "m_questPoolState") is IEnumerable enumerable))
		{
			return Array.Empty<QuestPoolStatusItem>();
		}
		List<QuestPoolStatusItem> list = new List<QuestPoolStatusItem>();
		foreach (object item in enumerable)
		{
			object obj = ((item is DictionaryEntry dictionaryEntry) ? dictionaryEntry.Value : ReadMember(item, "Value"));
			if (obj != null)
			{
				int num = ReadMemberAsInt(obj, "QuestPoolId", 0);
				if (taskPoolTypes.TryGetValue(num, out var value) && (string.Equals(value, "DAILY", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "WEEKLY", StringComparison.OrdinalIgnoreCase)))
				{
					list.Add(new QuestPoolStatusItem
					{
						PoolId = num,
						PoolType = value,
						RerollAvailableCount = Math.Max(0, ReadMemberAsInt(obj, "RerollAvailableCount", 0)),
						SecondsUntilNextGrant = Math.Max(0, ReadMemberAsInt(obj, "SecondsUntilNextGrant", 0)),
						BankedQuestCount = Math.Max(0, ReadMemberAsInt(obj, "BankedQuestCount", 0))
					});
				}
			}
		}
		return list.OrderBy((QuestPoolStatusItem item) => item.PoolType, StringComparer.Ordinal).ThenBy((QuestPoolStatusItem item) => item.PoolId).ToArray();
	}


	private SetRotationTransitionResult TryHandleSetRotationTransitionCore()
	{
		SetRotationTransitionResult setRotationTransitionResult = new SetRotationTransitionResult();
		if (IsActiveGameInProgress())
		{
			return setRotationTransitionResult;
		}
		GameObject val = GameObject.Find("/TheBox(Clone)");
		setRotationTransitionResult.BoxState = ReadBoxState(val);
		if (string.Equals(setRotationTransitionResult.BoxState, "SET_ROTATION", StringComparison.OrdinalIgnoreCase))
		{
			GameObject val2 = GameObject.Find("/TheBox(Clone)/RootObject/TheBox_Outer/TheBox_CoverRight_mesh/TheBox_CenterDisk/TheBox_CenterDisk_SetRotation(Clone)/SetRotationButton");
			string text = (((Object)(object)val2 == (Object)null) ? null : (TryInvokePegUiPressRelease(val2) ?? TryInvokePegUiClick(val2) ?? TryInvokeCommonClickMethod(val2)));
			setRotationTransitionResult.Visible = true;
			setRotationTransitionResult.Dismissed = text != null;
			setRotationTransitionResult.Stage = "EnterSetRotationTutorial";
			setRotationTransitionResult.Target = Describe(val2 ?? val, includeComponents: false);
			setRotationTransitionResult.Method = text ?? "";
			setRotationTransitionResult.Error = ((text == null) ? "The set-rotation tutorial entry button was not available." : "");
			return setRotationTransitionResult;
		}
		GameObject val3 = GameObject.Find("/Tournament(Clone)/DeckPickerTray(Clone)");
		if ((Object)(object)val3 == (Object)null || !val3.activeInHierarchy)
		{
			return setRotationTransitionResult;
		}
		Component val4 = val3.GetComponents<Component>().FirstOrDefault((Component component) => (Object)(object)component != (Object)null && ComponentTypeMatches(component, "DeckPickerTrayDisplay"));
		setRotationTransitionResult.TutorialState = ReadMember(val4, "m_setRotationTutorialState")?.ToString() ?? "";
		if (_setRotationReturnInFlight)
		{
			setRotationTransitionResult.Visible = true;
			setRotationTransitionResult.Pending = true;
			setRotationTransitionResult.Stage = "CompletingSetRotationTutorial";
			setRotationTransitionResult.Target = Describe(val3, includeComponents: false);
			return setRotationTransitionResult;
		}
		if (!string.Equals(setRotationTransitionResult.TutorialState, "READY", StringComparison.OrdinalIgnoreCase))
		{
			return setRotationTransitionResult;
		}
		setRotationTransitionResult.Visible = true;
		setRotationTransitionResult.Pending = true;
		setRotationTransitionResult.Stage = "CompletingSetRotationTutorial";
		setRotationTransitionResult.Target = Describe(val3, includeComponents: false);
		if (!TryInvokeZeroArgMethod(val4, "MarkSetRotationComplete", out var methodName, out var error))
		{
			setRotationTransitionResult.Failed = true;
			setRotationTransitionResult.Error = error;
			return setRotationTransitionResult;
		}
		if (!TryInvokeZeroArgMethod(val4, "OnWelcomeQuestDismiss", out var methodName2, out var error2))
		{
			setRotationTransitionResult.Failed = true;
			setRotationTransitionResult.Error = error2;
			return setRotationTransitionResult;
		}
		_setRotationReturnInFlight = true;
		((MonoBehaviour)this).StartCoroutine(FinishSetRotationReturn(val4));
		setRotationTransitionResult.Dismissed = true;
		setRotationTransitionResult.Returned = true;
		setRotationTransitionResult.Method = methodName + "; " + methodName2 + "; DeckPickerTrayDisplay.BackOutToHub()";
		return setRotationTransitionResult;
	}


	private IEnumerator FinishSetRotationReturn(Component deckPicker)
	{
		yield return (object)new WaitForSecondsRealtime(4f);
		try
		{
			if (!IsActiveGameInProgress() && (Object)(object)deckPicker != (Object)null && FindReturningDeckPicker() == deckPicker && ReadMember(deckPicker, "m_setRotationTutorialState")?.ToString() == "INACTIVE")
			{
				if (TryInvokeZeroArgMethod(deckPicker, "BackOutToHub", out var methodName, out var error))
				{
					Logger.LogInfo("Completed set-rotation tutorial and returned to hub via " + methodName + ".");
				}
				else
				{
					Logger.LogWarning("Unable to return from set-rotation tutorial: " + error);
				}
			}
		}
		finally
		{
			_setRotationReturnInFlight = false;
		}
	}


	private BridgeResponse DismissRewardPopup(BridgeRequest request)
	{
		List<RewardPopupDismissResult> list = new List<RewardPopupDismissResult>();
		RewardPopupDismissResult rewardPopupDismissResult = DismissRewardScrollPopupCore(GetBool(request.ArgumentsJson, "requireMainMenuReady", defaultValue: false));
		list.Add(rewardPopupDismissResult);
		if (rewardPopupDismissResult.Visible || rewardPopupDismissResult.Dismissed)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult, list));
		}
		RewardPopupDismissResult rewardPopupDismissResult2 = TryAcceptCardReplacementPopup();
		if (rewardPopupDismissResult2.Visible)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult2));
		}
		RewardPopupDismissResult rewardPopupDismissResult3 = DismissSeasonEndRewardPopupCore();
		list.Add(rewardPopupDismissResult3);
		if (rewardPopupDismissResult3.Dismissed || rewardPopupDismissResult3.Visible)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult3, list));
		}
		RewardPopupDismissResult rewardPopupDismissResult4 = DismissRankedRewardDisplayCore();
		list.Add(rewardPopupDismissResult4);
		if (rewardPopupDismissResult4.Dismissed || rewardPopupDismissResult4.Visible)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult4, list));
		}
		RewardPopupDismissResult rewardPopupDismissResult5 = DismissDeckRewardPopupCore();
		list.Add(rewardPopupDismissResult5);
		if (rewardPopupDismissResult5.Dismissed)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult5, list));
		}
		RewardPopupDismissResult rewardPopupDismissResult6 = DismissBoosterPackRewardPopupCore();
		list.Add(rewardPopupDismissResult6);
		if (rewardPopupDismissResult6.Dismissed)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult6, list));
		}
		RewardPopupDismissResult rewardPopupDismissResult7 = DismissForgotRewardsPopupCore();
		list.Add(rewardPopupDismissResult7);
		if (rewardPopupDismissResult7.Dismissed)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult7, list));
		}
		RewardPopupDismissResult rewardPopupDismissResult8 = DismissGenericRewardPopupCore();
		list.Add(rewardPopupDismissResult8);
		if (rewardPopupDismissResult8.Dismissed)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult8, list));
		}
		if (GetBool(request.ArgumentsJson, "requireMainMenuReady", defaultValue: false) && !list.Any((RewardPopupDismissResult item) => item.Visible))
		{
			object obj = InvokeNoArg(GetRewardTrackManager(), "GetRewardPresenter");
			if (TryInvokeBool(obj, "HasReward") && !TryInvokeBool(obj, "IsShowingReward"))
			{
				obj.GetType().GetMethod("ShowNextReward", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(Action) }, null)?.Invoke(obj, new object[1]);
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = false,
					kind = "RewardTrackPresenter"
				});
			}
		}
		if (GetBool(request.ArgumentsJson, "includeEndOfGameXp", defaultValue: true))
		{
			RewardPopupDismissResult rewardPopupDismissResult9 = DismissEndOfGameXpRewardCore();
			list.Add(rewardPopupDismissResult9);
			RewardPopupDismissResult result;
			if (rewardPopupDismissResult9.Dismissed || rewardPopupDismissResult9.Visible)
			{
				result = rewardPopupDismissResult9;
			}
			else if (rewardPopupDismissResult8.Visible)
			{
				result = rewardPopupDismissResult8;
			}
			else if (rewardPopupDismissResult7.Visible)
			{
				result = rewardPopupDismissResult7;
			}
			else
			{
				result = (rewardPopupDismissResult6.Visible ? rewardPopupDismissResult6 : rewardPopupDismissResult);
			}
			return BridgeResponse.Success(ToRewardPopupDismissPayload(result, list));
		}
		RewardPopupDismissResult result2;
		if (rewardPopupDismissResult8.Visible)
		{
			result2 = rewardPopupDismissResult8;
		}
		else if (rewardPopupDismissResult7.Visible)
		{
			result2 = rewardPopupDismissResult7;
		}
		else
		{
			result2 = (rewardPopupDismissResult6.Visible ? rewardPopupDismissResult6 : rewardPopupDismissResult);
		}
		return BridgeResponse.Success(ToRewardPopupDismissPayload(result2, list));
	}


	private RewardPopupDismissResult DismissRankedRewardDisplayCore()
	{
		Component val = EnumerateObjects(sort: false).SelectMany((GameObject gameObject) => gameObject.GetComponents<Component>()).FirstOrDefault((Component component) => (Object)(object)component != (Object)null && ComponentTypeMatches(component, "RankedRewardDisplay") && IsComponentActive(component));
		if ((Object)(object)val == (Object)null)
		{
			_rankedRewardDismissedInstanceId = 0;
			return new RewardPopupDismissResult
			{
				Kind = "RankedRewardDisplay",
				Visible = false,
				Dismissed = false
			};
		}
		int instanceID = ((Object)val).GetInstanceID();
		if (_rankedRewardDismissedInstanceId == instanceID)
		{
			return new RewardPopupDismissResult
			{
				Kind = "RankedRewardDisplay",
				Visible = false,
				Dismissed = false,
				Target = Describe(val.gameObject, includeComponents: false),
				Method = "Hide (confirmed)"
			};
		}
		bool flag = TryInvokeZeroArgMethod(val, "Hide", out var methodName, out var error);
		if (flag)
		{
			_rankedRewardDismissedInstanceId = instanceID;
		}
		return new RewardPopupDismissResult
		{
			Kind = "RankedRewardDisplay",
			Visible = true,
			Dismissed = flag,
			Target = Describe(val.gameObject, includeComponents: false),
			Method = (methodName ?? ""),
			Error = (error ?? "")
		};
	}


	private RewardPopupDismissResult DismissSeasonEndRewardPopupCore()
	{
		Component val = (from val7 in EnumerateObjects(sort: false).SelectMany((GameObject gameObject) => gameObject.GetComponents<Component>())
			where (Object)(object)val7 != (Object)null && ComponentTypeMatches(val7, "RewardBoxesDisplay") && IsComponentActive(val7)
			orderby val7.transform.GetSiblingIndex() descending, ((Object)val7).GetInstanceID()
			select val7).FirstOrDefault();
		if ((Object)(object)val != (Object)null)
		{
			GameObject val2 = ReadGameObjectMember(val, "m_Root") ?? val.gameObject;
			object obj = ReadMember(val, "m_DoneButton");
			Component val3 = (Component)((obj is Component) ? obj : null);
			bool flag = ReadMemberAsBool(val, "m_doneButtonFinishedShown", defaultValue: false);
			var anon = (from gameObject in (((ReadMember(val, "m_rewardPackageInstances") ?? ReadMember(val, "m_RewardObjects")) is IEnumerable source) ? (from gameObject in source.Cast<object>().Select((Func<object, GameObject>)((object item) =>
					{
						GameObject val7 = (GameObject)((item is GameObject) ? item : null);
						if (val7 != null)
						{
							return val7;
						}
						Component val8 = (Component)((item is Component) ? item : null);
						return (val8 != null) ? val8.gameObject : null;
					}))
					where (Object)(object)gameObject != (Object)null
					select gameObject).ToArray() : (from transform in val2.GetComponentsInChildren<Transform>(true)
					where (Object)(object)transform != (Object)null && (Object)(object)((Component)transform).gameObject != (Object)null
					select ((Component)transform).gameObject into gameObject
					where (Object)(object)FindComponentByName(gameObject, "RewardPackage") != (Object)null
					select gameObject).ToArray()).Distinct()
				select new
				{
					GameObject = gameObject,
					Package = FindComponentByName(gameObject, "RewardPackage")
				} into item
				where (Object)(object)item.Package != (Object)null && IsComponentActive(item.Package) && TryInvokeBool(item.Package, "IsEnabled")
				orderby ReadMemberAsInt(item.Package, "m_RewardIndex", int.MaxValue)
				select item).FirstOrDefault();
			if (anon != null)
			{
				bool dismissed = TryInvokeZeroArgMethod(anon.Package, "OpenReward", out var methodName, out var error);
				return new RewardPopupDismissResult
				{
					Kind = "SeasonEndRewardPackage",
					Visible = true,
					Dismissed = dismissed,
					Target = Describe(anon.GameObject, includeComponents: false),
					Method = (methodName ?? ""),
					Error = (error ?? "")
				};
			}
			if (flag && IsComponentActive(val3))
			{
				string text = TryInvokePegUiClick(val3.gameObject) ?? TryInvokePegUiPressRelease(val3.gameObject) ?? TryInvokeCommonClickMethod(val3.gameObject);
				return new RewardPopupDismissResult
				{
					Kind = "SeasonEndRewardPage",
					Visible = true,
					Dismissed = (text != null),
					Target = Describe(val3.gameObject, includeComponents: false),
					Method = (text ?? ""),
					Error = ((text == null) ? "The season-end reward Done button is visible but not clickable." : "")
				};
			}
			return new RewardPopupDismissResult
			{
				Kind = "SeasonEndRewardAnimation",
				Visible = true,
				Dismissed = false,
				Target = Describe(val2, includeComponents: false),
				Error = (flag ? "The reward page is complete but its Done button is still animating." : "Waiting for all season-end reward packages to finish opening.")
			};
		}
		var anon2 = (from gameObject in EnumerateObjects(sort: false)
			select new
			{
				GameObject = gameObject,
				Component = gameObject.GetComponents<Component>().FirstOrDefault((Component val7) => (Object)(object)val7 != (Object)null && ComponentTypeMatches(val7, "SeasonEndDialog"))
			}).FirstOrDefault(item => (Object)(object)item.Component != (Object)null && item.GameObject.activeInHierarchy);
		if (anon2 == null)
		{
			return new RewardPopupDismissResult
			{
				Kind = "SeasonEndReward",
				Visible = false,
				Dismissed = false
			};
		}
		Component component = anon2.Component;
		string text2 = ReadMemberAsString(component, "m_currentMode") ?? "";
		string text3 = ReadMemberAsString(component, "m_showAnimState") ?? "";
		if (string.Equals(text2, "CHEST_EARNED", StringComparison.OrdinalIgnoreCase))
		{
			if (ReadMemberAsBool(component, "m_chestOpened", defaultValue: false))
			{
				return new RewardPopupDismissResult
				{
					Kind = "SeasonEndRewardChestAnimation",
					Visible = true,
					Dismissed = false,
					Target = Describe(anon2.GameObject, includeComponents: false),
					Error = "Waiting for the season-end reward chest to create its reward pages."
				};
			}
			GameObject val4 = (from transform in anon2.GameObject.GetComponentsInChildren<Transform>(true)
				where (Object)(object)transform != (Object)null && (Object)(object)((Component)transform).gameObject != (Object)null
				select ((Component)transform).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && GetPath(gameObject).EndsWith("/Root/RankedRewardChest3D/RankedRewardChest3D/ClickBlocker", StringComparison.OrdinalIgnoreCase) && gameObject.GetComponents<Component>().Any((Component val7) => (Object)(object)val7 != (Object)null && (ComponentTypeMatches(val7, "Clickable") || ComponentTypeMatches(val7, "PegUIElement"))));
			string text4 = (((Object)(object)val4 == (Object)null) ? null : (TryInvokePegUiClick(val4) ?? TryInvokePegUiPressRelease(val4) ?? TryInvokeCommonClickMethod(val4)));
			return new RewardPopupDismissResult
			{
				Kind = "SeasonEndRewardChest",
				Visible = true,
				Dismissed = (text4 != null),
				Target = Describe(val4 ?? anon2.GameObject, includeComponents: false),
				Method = (text4 ?? ""),
				Error = ((text4 == null) ? "The season-end reward chest click blocker is not ready." : "")
			};
		}
		if (ReadMemberAsBool(component, "m_isOkayButtonHidden", defaultValue: false) || (!string.IsNullOrWhiteSpace(text3) && !string.Equals(text3, "FINISHED", StringComparison.OrdinalIgnoreCase)))
		{
			RewardPopupDismissResult rewardPopupDismissResult = new RewardPopupDismissResult();
			rewardPopupDismissResult.Kind = "SeasonEndRewardDialogAnimation";
			rewardPopupDismissResult.Visible = true;
			rewardPopupDismissResult.Dismissed = false;
			rewardPopupDismissResult.Target = Describe(anon2.GameObject, includeComponents: false);
			rewardPopupDismissResult.Error = "Waiting for the season-end dialog animation (" + text2 + "/" + text3 + ").";
			return rewardPopupDismissResult;
		}
		object obj2 = ReadMember(component, "m_okayButton");
		Component val5 = (Component)((obj2 is Component) ? obj2 : null);
		GameObject val6 = (GameObject)(IsComponentActive(val5) ? ((object)val5.gameObject) : ((object)(from transform in anon2.GameObject.GetComponentsInChildren<Transform>(true)
			where (Object)(object)transform != (Object)null && (Object)(object)((Component)transform).gameObject != (Object)null
			select ((Component)transform).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "BackButtonV3_Dynamic", StringComparison.OrdinalIgnoreCase) && gameObject.GetComponents<Component>().Any((Component val7) => (Object)(object)val7 != (Object)null && ComponentTypeMatches(val7, "UIBButton")))));
		string text5 = (((Object)(object)val6 == (Object)null) ? null : (TryInvokePegUiClick(val6) ?? TryInvokePegUiPressRelease(val6) ?? TryInvokeCommonClickMethod(val6)));
		return new RewardPopupDismissResult
		{
			Kind = "SeasonEndRewardDialog",
			Visible = true,
			Dismissed = (text5 != null),
			Target = Describe(val6 ?? anon2.GameObject, includeComponents: false),
			Method = (text5 ?? ""),
			Error = ((text5 == null) ? ("The season-end dialog Continue button is not ready (" + text2 + ").") : "")
		};
	}


	private RewardPopupDismissResult DismissDeckRewardPopupCore()
	{
		GameObject val = EnumerateObjects().FirstOrDefault((GameObject gameObject) => IsVisiblePopupComponent(gameObject, "QuestToast"));
		if ((Object)(object)val == (Object)null)
		{
			return new RewardPopupDismissResult
			{
				Kind = "DeckReward",
				Visible = false,
				Dismissed = false
			};
		}
		GameObject val2 = (from item in val.GetComponentsInChildren<Transform>(true)
			where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
			select ((Component)item).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "ClickCatcher", StringComparison.OrdinalIgnoreCase) && GetPath(gameObject).IndexOf("/QuestToast(Clone)/ClickCatcher", StringComparison.OrdinalIgnoreCase) >= 0 && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "PegUIElement")));
		string text = (((Object)(object)val2 == (Object)null) ? null : (TryInvokePegUiClick(val2) ?? TryInvokeCommonClickMethod(val2)));
		if (text == null)
		{
			Component val3 = (from item in val.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "QuestToast"));
			if ((Object)(object)val3 != (Object)null && TryInvokeZeroArgMethod(val3, "CloseQuestToast", out var methodName, out var _))
			{
				text = methodName;
			}
		}
		return new RewardPopupDismissResult
		{
			Kind = "DeckReward",
			Visible = true,
			Dismissed = (text != null),
			Target = Describe(val2 ?? val, includeComponents: false),
			Method = (text ?? ""),
			Error = ((text == null) ? "The verified QuestToast ClickCatcher/CloseQuestToast was not available." : "")
		};
	}


	private BridgeResponse DismissRewardScrollPopup()
	{
		return BridgeResponse.Success(ToRewardPopupDismissPayload(DismissRewardScrollPopupCore()));
	}


	private RewardPopupDismissResult DismissRewardScrollPopupCore()
	{
		return DismissRewardScrollPopupCore(requireInitialized: false);
	}


	private RewardPopupDismissResult DismissRewardScrollPopupCore(bool requireInitialized)
	{
		Component val = FindVisibleRewardScrollComponent();
		if ((Object)(object)val != (Object)null)
		{
			return DismissRewardScrollComponent(val, requireInitialized);
		}
		GameObject val2 = ResolveObject(Selector.From("{\"name\":\"Clickable\",\"pathContains\":\"/RewardScroll Popup Bone/RewardScroll/Clickable\"}"));
		if ((Object)(object)val2 == (Object)null)
		{
			return new RewardPopupDismissResult
			{
				Kind = "RewardScroll",
				Visible = false,
				Dismissed = false
			};
		}
		string text = TryInvokePegUiPressRelease(val2);
		return new RewardPopupDismissResult
		{
			Kind = "RewardScroll",
			Visible = true,
			Dismissed = (text != null),
			Target = Describe(val2, includeComponents: false),
			Method = (text ?? "")
		};
	}


	private static RewardPopupDismissResult DismissRewardScrollComponent(Component rewardScroll, bool requireInitialized)
	{
		if (requireInitialized && !(ReadMember(rewardScroll, "OnRewardScrollHidden") is Delegate))
		{
			return new RewardPopupDismissResult
			{
				Kind = "RewardScroll",
				Visible = true,
				Dismissed = false
			};
		}
		bool dismissed = TryInvokeSingleArgMethod(rewardScroll, "Hide", false, out var methodName, out var error);
		return new RewardPopupDismissResult
		{
			Kind = "RewardScroll",
			Visible = true,
			Dismissed = dismissed,
			Target = Describe(rewardScroll.gameObject, includeComponents: false),
			Method = (methodName ?? ""),
			Error = (error ?? "")
		};
	}


	private static Component FindVisibleRewardScrollComponent()
	{
		Type type = FindLoadedComponentType("Hearthstone.Progression.RewardScroll");
		if (type == null)
		{
			return null;
		}
		return Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault((Component component) => (Object)(object)component != (Object)null && TryGetGameObject(component, out var gameObject) && IsBridgeVisibleObject(gameObject));
	}


	private RewardPopupDismissResult DismissBoosterPackRewardPopupCore()
	{
		GameObject[] array = FindVisibleBoosterPackRewardObjects().Take(4).ToArray();
		if (array.Length == 0)
		{
			return new RewardPopupDismissResult
			{
				Kind = "BoosterPackReward",
				Visible = false,
				Dismissed = false
			};
		}
		GameObject[] array2 = array;
		foreach (GameObject val in array2)
		{
			Component val2 = (from item in val.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "BoosterPackReward"));
			object obj2;
			if (!((Object)(object)val2 == (Object)null))
			{
				object obj = ReadMember(val2, "m_clickCatcher");
				obj2 = ((obj is Component) ? obj : null);
			}
			else
			{
				obj2 = null;
			}
			Component val3 = (Component)obj2;
			if ((Object)(object)val3 != (Object)null && TryGetGameObject(val3, out var gameObject))
			{
				string text = TryInvokePegUiClick(gameObject) ?? TryInvokePegUiPressRelease(gameObject);
				if (text != null)
				{
					return new RewardPopupDismissResult
					{
						Kind = "BoosterPackReward",
						Visible = true,
						Dismissed = true,
						Target = Describe(gameObject, includeComponents: false),
						Method = text
					};
				}
			}
			if ((Object)(object)val2 != (Object)null && (TryInvokeZeroArgMethod(val2, "HideReward", out var methodName, out var error) || TryInvokeZeroArgMethod(val2, "HideWithFX", out methodName, out error)))
			{
				return new RewardPopupDismissResult
				{
					Kind = "BoosterPackReward",
					Visible = true,
					Dismissed = true,
					Target = Describe(val, includeComponents: false),
					Method = methodName,
					Error = (error ?? "")
				};
			}
			Transform val4 = val.transform.Find("Root/ClickCatcher");
			GameObject val5 = ((val4 != null) ? ((Component)val4).gameObject : null);
			string text2 = (((Object)(object)val5 == (Object)null) ? null : (TryInvokePegUiPressRelease(val5) ?? TryInvokePegUiClick(val5)));
			if (text2 != null)
			{
				return new RewardPopupDismissResult
				{
					Kind = "BoosterPackReward",
					Visible = true,
					Dismissed = true,
					Target = Describe(val5, includeComponents: false),
					Method = text2
				};
			}
		}
		return new RewardPopupDismissResult
		{
			Kind = "BoosterPackReward",
			Visible = true,
			Dismissed = false,
			Target = Describe(array[0], includeComponents: false),
			Error = "No supported BoosterPackReward close method was found."
		};
	}


	private RewardPopupDismissResult DismissForgotRewardsPopupCore()
	{
		GameObject val = EnumerateObjects().FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "RewardTrackForgotRewardsPopup")));
		if ((Object)(object)val == (Object)null)
		{
			return new RewardPopupDismissResult
			{
				Kind = "RewardTrackForgotRewardsPopup",
				Visible = false,
				Dismissed = false
			};
		}
		GameObject val2 = (from item in val.GetComponentsInChildren<Transform>(true)
			where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
			select ((Component)item).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "ButtonFramed", StringComparison.OrdinalIgnoreCase) && HasDescendantText(gameObject, "GLOBAL_BUTTON_OK", "确定"));
		string text = (((Object)(object)val2 == (Object)null) ? null : (TryInvokePegUiPressRelease(val2) ?? TryInvokePegUiClick(val2)));
		return new RewardPopupDismissResult
		{
			Kind = "RewardTrackForgotRewardsPopup",
			Visible = true,
			Dismissed = (text != null),
			Target = Describe(val2 ?? val, includeComponents: false),
			Method = (text ?? ""),
			Error = ((text == null) ? "The verified GLOBAL_BUTTON_OK confirmation button was not available." : "")
		};
	}


	private RewardPopupDismissResult DismissGenericRewardPopupCore()
	{
		GameObject[] array = (from gameObject2 in (from val2 in EnumerateObjects()
				where !ContainsAny(((Object)val2).name + " " + GetPath(val2), "RankedRewardDisplay")
				select val2).Where((GameObject val2) =>
			{
				string name = ((Object)val2).name ?? "";
				string path = GetPath(val2);
				bool activeInHierarchy = val2.activeInHierarchy;
				return RewardPopupObjectPolicy.IsLikelyBlocking(name, path, activeInHierarchy) && RewardPopupObjectPolicy.IsLikelyBlocking(name, path, activeInHierarchy, IsHiddenBoosterPackRewardObject(val2));
			})
			orderby GetPath(gameObject2).Length
			select gameObject2).Take(12).ToArray();
		if (array.Length == 0)
		{
			return new RewardPopupDismissResult
			{
				Kind = "GenericReward",
				Visible = false,
				Dismissed = false
			};
		}
		GameObject[] array2 = array;
		foreach (GameObject val in array2)
		{
			GameObject[] array3 = (from val2 in (from transform in val.GetComponentsInChildren<Transform>(true)
					where (Object)(object)transform != (Object)null && (Object)(object)((Component)transform).gameObject != (Object)null
					select ((Component)transform).gameObject into val2
					where val2.activeInHierarchy && IsRewardDismissClickTarget(val2)
					select val2).Concat(((IEnumerable<GameObject>)new GameObject[1] { val }).Where((Func<GameObject, bool>)IsRewardDismissClickTarget))
				group val2 by ((Object)val2).GetInstanceID() into @group
				select @group.First()).OrderBy(RewardDismissTargetPriority).ThenBy(GetPath).ToArray();
			foreach (GameObject gameObject in array3)
			{
				string text = TryInvokePegUiPressRelease(gameObject) ?? TryInvokePegUiClick(gameObject) ?? TryInvokeCommonClickMethod(gameObject);
				if (text != null)
				{
					return new RewardPopupDismissResult
					{
						Kind = "GenericReward",
						Visible = true,
						Dismissed = true,
						Target = Describe(gameObject, includeComponents: false),
						Method = text
					};
				}
			}
		}
		return new RewardPopupDismissResult
		{
			Kind = "GenericReward",
			Visible = true,
			Dismissed = false,
			Target = Describe(array[0], includeComponents: false),
			Error = "No safe internal reward dismiss target was found."
		};
	}


	private RewardPopupDismissResult DismissEndOfGameXpRewardCore()
	{
		object obj = InvokeStaticNoArg("Hearthstone.Progression.RewardXpNotificationManager", "Get");
		if (obj != null)
		{
			object obj2 = InvokeNoArg(obj, "get_IsShowingXpGains");
			if (obj2 is bool && (bool)obj2)
			{
				obj2 = InvokeNoArg(obj, "get_JustShowGameXp");
				bool flag = default;
				int num;
				if (obj2 is bool)
				{
					flag = (bool)obj2;
					num = 1;
				}
				else
				{
					num = 0;
				}
				bool flag2 = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
				if (!flag2)
				{
					return new RewardPopupDismissResult
					{
						Kind = "EndOfGameRewardXp",
						Visible = true,
						Dismissed = false,
						Target = new
						{
							type = (obj.GetType().FullName ?? obj.GetType().Name)
						},
						Error = "XP notifications are active but are not paused in the end-of-game game-XP flow."
					};
				}
				bool dismissed = TryInvokeZeroArgMethod(obj, "ContinueNotifications", out var methodName, out var error);
				return new RewardPopupDismissResult
				{
					Kind = "EndOfGameRewardXp",
					Visible = true,
					Dismissed = dismissed,
					Target = new
					{
						type = (obj.GetType().FullName ?? obj.GetType().Name),
						justShowGameXp = flag2
					},
					Method = (methodName ?? ""),
					Error = (error ?? "")
				};
			}
		}
		return new RewardPopupDismissResult
		{
			Kind = "EndOfGameRewardXp",
			Visible = false,
			Dismissed = false
		};
	}


	private static bool IsRewardDismissClickTarget(GameObject gameObject)
	{
		string text = ((Object)gameObject).name + " " + GetPath(gameObject);
		if (ContainsAny(text, "Claim", "Choose", "Purchase", "Shop", "RewardTrackButton"))
		{
			return false;
		}
		return ContainsAny(text, "Clickable", "ClickCatcher", "Hitbox", "Continue", "DoneButton", "CloseButton", "Dismiss", "Button_OK", "Button_Continue");
	}


	private static int RewardDismissTargetPriority(GameObject gameObject)
	{
		string text = ((Object)gameObject).name + " " + GetPath(gameObject);
		if (ContainsAny(text, "Clickable", "ClickCatcher"))
		{
			return 0;
		}
		if (ContainsAny(text, "Continue", "DoneButton", "Button_OK"))
		{
			return 1;
		}
		if (ContainsAny(text, "CloseButton", "Dismiss"))
		{
			return 2;
		}
		return 3;
	}


	private static object ToRewardPopupDismissPayload(RewardPopupDismissResult result)
	{
		return ToRewardPopupDismissPayload(result, null);
	}


	private static object ToRewardPopupDismissPayload(RewardPopupDismissResult result, IReadOnlyCollection<RewardPopupDismissResult> attempts)
	{
		bool visible = result.Visible;
		bool dismissed = result.Dismissed;
		string kind = result.Kind ?? "";
		object target = result.Target;
		string method = result.Method ?? "";
		string error = result.Error ?? "";
		object[] array = attempts?.Select((RewardPopupDismissResult item) => new
		{
			visible = item.Visible,
			dismissed = item.Dismissed,
			kind = (item.Kind ?? ""),
			method = (item.Method ?? ""),
			error = (item.Error ?? "")
		}).ToArray();
		return new
		{
			visible = visible,
			dismissed = dismissed,
			kind = kind,
			target = target,
			method = method,
			error = error,
			attempts = (array ?? Array.Empty<object>())
		};
	}


	private BridgeResponse DismissInGameMessageModal()
	{
		GameObject val = ResolveObject(Selector.From("{\"component\":\"Hearthstone.InGameMessage.UI.MessageModal\"}"));
		if ((Object)(object)val == (Object)null)
		{
			GameObject val2 = ResolveObject(Selector.From("{\"component\":\"EventEndedPopup\"}"));
			Component val3 = (((Object)(object)val2 == (Object)null) ? null : (from item in val2.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "EventEndedPopup")));
			if ((Object)(object)val3 != (Object)null && TryInvokeZeroArgMethod(val3, "Hide", out var methodName, out var error))
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "EventEndedPopup",
					target = Describe(val2, includeComponents: false),
					method = methodName,
					error = (error ?? "")
				});
			}
			return BridgeResponse.Success(new
			{
				visible = false,
				dismissed = false,
				method = ""
			});
		}
		Component val4 = (from item in val.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "Hearthstone.InGameMessage.UI.MessageModal"));
		if ((Object)(object)val4 != (Object)null && (TryInvokeZeroArgMethod(val4, "OnClosePressed", out var methodName2, out var error2) || TryInvokeZeroArgMethod(val4, "ForceClose", out methodName2, out error2)))
		{
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = true,
				target = Describe(val, includeComponents: false),
				method = methodName2,
				error = ""
			});
		}
		GameObject val5 = ResolveObject(Selector.From("{\"name\":\"ButtonFramed\",\"pathContains\":\"/MessageModal/Root/BoardRoot/Button_Framed/Button_Framed/ButtonFramed\"}"));
		string text = (((Object)(object)val5 == (Object)null) ? null : TryInvokePegUiPressRelease(val5));
		return BridgeResponse.Success(new
		{
			visible = true,
			dismissed = (text != null),
			target = Describe(val, includeComponents: false),
			method = (text ?? ""),
			error = (((Object)(object)val4 == (Object)null) ? "MessageModal component not found." : "")
		});
	}


	private BridgeResponse DismissNavigationPopup(BridgeRequest request)
	{
		Component val = FindVisibleRewardScrollComponent();
		if ((Object)(object)val != (Object)null)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(DismissRewardScrollComponent(val, requireInitialized: true)));
		}
		RewardPopupDismissResult rewardPopupDismissResult = DismissRotatedBoostersPopup();
		if (rewardPopupDismissResult.Visible)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult));
		}
		SetRotationTransitionResult setRotationTransitionResult = TryHandleSetRotationTransitionCore();
		if (setRotationTransitionResult.Failed)
		{
			return BridgeResponse.Failure(setRotationTransitionResult.Error);
		}
		if (setRotationTransitionResult.Visible)
		{
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = setRotationTransitionResult.Dismissed,
				pending = setRotationTransitionResult.Pending,
				kind = "SetRotationTransition",
				stage = setRotationTransitionResult.Stage,
				boxState = setRotationTransitionResult.BoxState,
				tutorialState = setRotationTransitionResult.TutorialState,
				target = setRotationTransitionResult.Target,
				method = setRotationTransitionResult.Method,
				error = setRotationTransitionResult.Error
			});
		}
		RewardPopupDismissResult rewardPopupDismissResult2 = TryAcceptCardReplacementPopup();
		if (rewardPopupDismissResult2.Visible)
		{
			return BridgeResponse.Success(ToRewardPopupDismissPayload(rewardPopupDismissResult2));
		}
		GameObject cardListPopup = EnumerateObjects().FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "CardListPopup(Clone)", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "CardListPopup")));
		if ((Object)(object)cardListPopup != (Object)null)
		{
			GameObject val2 = (from item in cardListPopup.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "OkayButton", StringComparison.OrdinalIgnoreCase) && string.Equals(GetPath(gameObject), GetPath(cardListPopup) + "/OkayButton", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).Any((Component item) => ComponentTypeMatches(item, "UIBButton")));
			string text = (((Object)(object)val2 == (Object)null) ? null : (TryInvokePegUiClick(val2) ?? TryInvokePegUiPressRelease(val2)));
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = (text != null),
				kind = "CardListPopup",
				target = Describe(val2 ?? cardListPopup, includeComponents: false),
				method = (text ?? ""),
				error = ((text == null) ? "The verified CardListPopup OkayButton was not available." : "")
			});
		}
		GameObject val3 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "AlertPopup(Clone)", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "AlertPopup")));
		if ((Object)(object)val3 != (Object)null)
		{
			GameObject[] array = (from gameObject in (from item in val3.GetComponentsInChildren<Component>(true)
					where (Object)(object)item != (Object)null && (Object)(object)item.gameObject != (Object)null && item.gameObject.activeInHierarchy && ComponentTypeMatches(item, "UIBButton")
					select item.gameObject).Distinct()
				orderby GetPath(gameObject).Length
				select gameObject).ToArray();
			GameObject val4 = array.FirstOrDefault((GameObject gameObject) => string.Equals(((Object)gameObject).name, "OkayButton", StringComparison.OrdinalIgnoreCase));
			bool directStrategySingleButtonFallback = false;
			if ((Object)(object)val4 == (Object)null && array.Length == 1 && GetBool(request.ArgumentsJson, "allowSingleButtonAlertPopupDismissal", defaultValue: false))
			{
				val4 = array[0];
				directStrategySingleButtonFallback = true;
			}
			string text2 = (((Object)(object)val4 == (Object)null) ? null : (TryInvokePegUiClick(val4) ?? TryInvokePegUiPressRelease(val4) ?? TryInvokeCommonClickMethod(val4)));
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = (text2 != null),
				kind = "AlertPopup",
				target = Describe(val4 ?? val3, includeComponents: false),
				method = (text2 ?? ""),
				directStrategySingleButtonFallback = directStrategySingleButtonFallback,
				buttons = array.Select((GameObject button) => Describe(button, includeComponents: false)).ToArray(),
				error = ((text2 == null) ? "The verified AlertPopup OkayButton was not available." : "")
			});
		}
		GameObject val5 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => IsVisiblePopupComponent(gameObject, "TutorialPreviewController"));
		if ((Object)(object)val5 != (Object)null)
		{
			GameObject val6 = (from item in val5.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject item) => item.activeInHierarchy && string.Equals(((Object)item).name, "BlueButtonContainer", StringComparison.OrdinalIgnoreCase));
			string text3 = (((Object)(object)val6 == (Object)null) ? null : (TryInvokePegUiPressRelease(val6) ?? TryInvokePegUiClick(val6) ?? TryInvokeCommonClickMethod(val6)));
			if (text3 != null)
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "TutorialPreviewWidget",
					target = Describe(val6, includeComponents: false),
					method = text3,
					error = ""
				});
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				kind = "TutorialPreviewWidget",
				target = Describe(val5, includeComponents: false),
				method = "",
				error = "Tutorial preview confirmation button was not available."
			});
		}
		GameObject val7 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "BaconTutorialPopup(Clone)", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "TutorialNotification")));
		if ((Object)(object)val7 != (Object)null)
		{
			GameObject val8 = (from item in val7.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject item) => item.activeInHierarchy && string.Equals(((Object)item).name, "StartButton", StringComparison.OrdinalIgnoreCase));
			string text4 = (((Object)(object)val8 == (Object)null) ? null : (TryInvokePegUiPressRelease(val8) ?? TryInvokePegUiClick(val8) ?? TryInvokeCommonClickMethod(val8)));
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = (text4 != null),
				kind = "BaconTutorialPopup",
				target = Describe(val8 ?? val7, includeComponents: false),
				method = (text4 ?? ""),
				error = ((text4 == null) ? "Battlegrounds tutorial continue button was not available." : "")
			});
		}
		GameObject val9 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "IGM_TrialCards(Clone)", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "TrialCardPeriodPopup")));
		if ((Object)(object)val9 != (Object)null)
		{
			GameObject val10 = (from item in val9.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "Button_right", StringComparison.OrdinalIgnoreCase) && HasDescendantText(gameObject, "GLOBAL_BUTTON_OK", "确定"));
			string text5 = (((Object)(object)val10 == (Object)null) ? null : (TryInvokePegUiPressRelease(val10) ?? TryInvokePegUiClick(val10)));
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = (text5 != null),
				kind = "TrialCardPeriodPopup",
				target = Describe(val10 ?? val9, includeComponents: false),
				method = (text5 ?? ""),
				error = ((text5 == null) ? "The verified trial-card GLOBAL_BUTTON_OK button was not available." : "")
			});
		}
		GameObject val11 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "LuckyDrawEventDialog(Clone)", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "LuckyDrawEventDialog")));
		if ((Object)(object)val11 != (Object)null)
		{
			Component val12 = (from item in val11.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "LuckyDrawEventDialog"));
			if ((Object)(object)val12 != (Object)null && TryInvokeZeroArgMethod(val12, "Hide", out var methodName, out var error))
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "LuckyDrawEventDialog",
					target = Describe(val11, includeComponents: false),
					method = methodName,
					error = (error ?? "")
				});
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				kind = "LuckyDrawEventDialog",
				target = Describe(val11, includeComponents: false),
				method = "",
				error = (((Object)(object)val12 == (Object)null) ? "LuckyDrawEventDialog component not found." : "LuckyDrawEventDialog.Hide() failed.")
			});
		}
		GameObject val13 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => IsVisiblePopupComponent(gameObject, "RankedIntroPopup"));
		if ((Object)(object)val13 != (Object)null)
		{
			Component val14 = (from item in val13.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "RankedIntroPopup"));
			if ((Object)(object)val14 != (Object)null && TryInvokeZeroArgMethod(val14, "Hide", out var methodName2, out var error2))
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "RankedIntroPopup",
					target = Describe(val13, includeComponents: false),
					method = methodName2,
					error = (error2 ?? "")
				});
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				kind = "RankedIntroPopup",
				target = Describe(val13, includeComponents: false),
				method = "",
				error = (((Object)(object)val14 == (Object)null) ? "RankedIntroPopup component not found." : "RankedIntroPopup.Hide() failed.")
			});
		}
		GameObject val15 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => IsVisiblePopupComponent(gameObject, "RankedBonusStarsPopup"));
		if ((Object)(object)val15 != (Object)null)
		{
			Component val16 = (from item in val15.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "RankedBonusStarsPopup"));
			if ((Object)(object)val16 != (Object)null && TryInvokeZeroArgMethod(val16, "Hide", out var methodName3, out var error3))
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "RankedBonusStarsPopup",
					target = Describe(val15, includeComponents: false),
					method = methodName3,
					error = (error3 ?? "")
				});
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				kind = "RankedBonusStarsPopup",
				target = Describe(val15, includeComponents: false),
				method = "",
				error = (((Object)(object)val16 == (Object)null) ? "RankedBonusStarsPopup component not found." : "RankedBonusStarsPopup.Hide() failed.")
			});
		}
		GameObject val17 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => IsVisiblePopupComponent(gameObject, "Hearthstone.Progression.QuestNotificationPopup"));
		if ((Object)(object)val17 != (Object)null)
		{
			GameObject val18 = (from item in val17.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && (string.Equals(((Object)gameObject).name, "OkayButton", StringComparison.OrdinalIgnoreCase) || HasDescendantText(gameObject, "GLOBAL_BUTTON_OK", "确定")) && (from item in gameObject.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).Any((Component item) => ComponentTypeMatches(item, "UIBButton")));
			string text6 = (((Object)(object)val18 == (Object)null) ? null : (TryInvokePegUiPressRelease(val18) ?? TryInvokePegUiClick(val18) ?? TryInvokeCommonClickMethod(val18)));
			if (text6 != null)
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "QuestNotificationPopup",
					target = Describe(val18, includeComponents: false),
					method = text6,
					error = ""
				});
			}
			Component val19 = (from item in val17.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).FirstOrDefault((Component item) => ComponentTypeMatches(item, "Hearthstone.Progression.QuestNotificationPopup"));
			if ((Object)(object)val19 != (Object)null && TryInvokeZeroArgMethod(val19, "Hide", out var methodName4, out var error4))
			{
				return BridgeResponse.Success(new
				{
					visible = true,
					dismissed = true,
					kind = "QuestNotificationPopup",
					target = Describe(val17, includeComponents: false),
					method = methodName4,
					error = (error4 ?? "")
				});
			}
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				kind = "QuestNotificationPopup",
				target = Describe(val17, includeComponents: false),
				method = "",
				error = (((Object)(object)val19 == (Object)null) ? "QuestNotificationPopup component not found." : "QuestNotificationPopup.Hide() failed.")
			});
		}
		GameObject val20 = EnumerateObjects().FirstOrDefault((GameObject gameObject) => IsVisiblePopupComponent(gameObject, "CompletedQuestsUpdatedPopup"));
		if ((Object)(object)val20 != (Object)null)
		{
			GameObject val21 = (from item in val20.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject gameObject) => gameObject.activeInHierarchy && string.Equals(((Object)gameObject).name, "OkayButton", StringComparison.OrdinalIgnoreCase) && (from item in gameObject.GetComponents<Component>()
				where (Object)(object)item != (Object)null
				select item).Any((Component item) => ComponentTypeMatches(item, "UIBButton")));
			string text7 = (((Object)(object)val21 == (Object)null) ? null : (TryInvokePegUiPressRelease(val21) ?? TryInvokePegUiClick(val21) ?? TryInvokeCommonClickMethod(val21)));
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = (text7 != null),
				kind = "CompletedQuestsUpdatedPopup",
				target = Describe(val21 ?? val20, includeComponents: false),
				method = (text7 ?? ""),
				error = ((text7 == null) ? "The verified CompletedQuestsUpdatedPopup OkayButton was not available." : "")
			});
		}
		return BridgeResponse.Success(new
		{
			visible = false,
			dismissed = false,
			kind = "",
			method = "",
			error = ""
		});
	}


	private static bool HasDescendantText(GameObject root, params string[] expectedText)
	{
		if ((Object)(object)root != (Object)null)
		{
			return (from item in root.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select item).Any((Transform item) => ContainsAny(GetObjectText(((Component)item).gameObject), expectedText));
		}
		return false;
	}


	private static bool IsVisiblePopupComponent(GameObject gameObject, string componentType)
	{
		if ((Object)(object)gameObject == (Object)null || !gameObject.activeInHierarchy)
		{
			return false;
		}
		if (GetPath(gameObject).IndexOf("Popup Bone", StringComparison.OrdinalIgnoreCase) < 0 && !(from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, "Hearthstone.UI.PopupRoot")))
		{
			return false;
		}
		return (from item in gameObject.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).Any((Component item) => ComponentTypeMatches(item, componentType));
	}


	private static object GetRewardTrackManager()
	{
		return InvokeStaticNoArg("Hearthstone.Progression.RewardTrackManager", "Get");
	}


	private static bool IsNetworkLoggedIn()
	{
		object obj = InvokeStaticNoArg("Network", "IsLoggedIn");
		bool flag = default;
		int num;
		if (obj is bool)
		{
			flag = (bool)obj;
			num = 1;
		}
		else
		{
			num = 0;
		}
		return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
	}


	private static IEnumerable<object> EnumerateRewardTracks(object manager)
	{
		if (manager.GetType().GetField("m_rewardTrackEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(manager) is IDictionary dictionary)
		{
			foreach (DictionaryEntry item in dictionary)
			{
				if (item.Value != null)
				{
					yield return item.Value;
				}
			}
		}
		object obj = InvokeNoArg(manager, "GetCurrentEventRewardTrack");
		if (obj != null)
		{
			yield return obj;
		}
	}


	private static void TryRefreshRewardTrack(object track)
	{
		try
		{
			InvokeNoArg(track, "UpdateRewardsAndBonuses");
		}
		catch
		{
		}
	}


	private BridgeResponse ClickObject(BridgeRequest request)
	{
		GameObject val = ResolveObject(Selector.From(ExtractObject(request.ArgumentsJson, "selector")));
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("Object not found.");
		}
		Button val2 = val.GetComponent<Button>() ?? val.GetComponentInChildren<Button>(true);
		if ((Object)(object)val2 != (Object)null)
		{
			((UnityEvent)val2.onClick).Invoke();
			return BridgeResponse.Success(new
			{
				clicked = Describe(val, includeComponents: true),
				method = "UnityEngine.UI.Button.onClick"
			});
		}
		string text = TryInvokePegUiClick(val) ?? TryInvokeCommonClickMethod(val);
		if (text != null)
		{
			return BridgeResponse.Success(new
			{
				clicked = Describe(val, includeComponents: true),
				method = text
			});
		}
		SendCommonClickMessages(val);
		return BridgeResponse.Success(new
		{
			clicked = Describe(val, includeComponents: true),
			method = "SendMessage click sequence"
		});
	}


	private BridgeResponse ProjectObject(BridgeRequest request)
	{
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_014d: Unknown result type (might be due to invalid IL or missing references)
		GameObject val = ResolveObject(Selector.From(ExtractObject(request.ArgumentsJson, "selector")));
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("Object not found.");
		}
		Camera[] array = Camera.allCameras.Where((Camera camera) => (Object)(object)camera != (Object)null && ((Behaviour)camera).isActiveAndEnabled).ToArray();
		if (array.Length == 0 && (Object)(object)Camera.main != (Object)null)
		{
			array = new Camera[1] { Camera.main };
		}
		if (array.Length == 0)
		{
			return BridgeResponse.Failure("No active camera found.");
		}
		Vector3 position = val.transform.position;
		var source = array.Select((Camera camera) =>
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_000c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_0037: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			Vector3 val2 = camera.WorldToScreenPoint(position);
			float num = ((Screen.width <= 0) ? 0f : (val2.x / (float)Screen.width));
			float num2 = ((Screen.height <= 0) ? 0f : (1f - val2.y / (float)Screen.height));
			bool inView = val2.z > 0f && num >= 0f && num <= 1f && num2 >= 0f && num2 <= 1f;
			return new
			{
				camera = camera,
				screen = val2,
				normalizedX = num,
				normalizedY = num2,
				inView = inView
			};
		}).ToArray();
		var anon = source.FirstOrDefault(item => item.inView && (Object)(object)item.camera == (Object)(object)Camera.main) ?? source.FirstOrDefault(item => item.inView) ?? source.First();
		return BridgeResponse.Success(new
		{
			target = Describe(val, includeComponents: false),
			camera = ((Object)anon.camera).name,
			screenWidth = Screen.width,
			screenHeight = Screen.height,
			screen = new
			{
				anon.screen.x,
				anon.screen.y,
				anon.screen.z
			},
			normalized = new
			{
				x = anon.normalizedX,
				y = anon.normalizedY
			},
			inView = anon.inView,
			candidates = source.Select(item =>
			{
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0017: Unknown result type (might be due to invalid IL or missing references)
				//IL_0022: Unknown result type (might be due to invalid IL or missing references)
				return new
				{
					camera = ((Object)item.camera).name,
					screen = new
					{
						item.screen.x,
						item.screen.y,
						item.screen.z
					},
					normalized = new
					{
						x = item.normalizedX,
						y = item.normalizedY
					},
					inView = item.inView
				};
			}).ToArray()
		});
	}


	private BridgeResponse ConstructedOpenFormatPicker()
	{
		try
		{
			object deckPickerTrayDisplay = GetDeckPickerTrayDisplay();
			if (deckPickerTrayDisplay == null)
			{
				return BridgeResponse.Failure("DeckPickerTrayDisplay is not available.");
			}
			if (!TryInvokeBool(deckPickerTrayDisplay, "CanSwitchFormats"))
			{
				return BridgeResponse.Failure("DeckPickerTrayDisplay.CanSwitchFormats() returned false.");
			}
			InvokeNoArg(deckPickerTrayDisplay, "SwitchFormatButtonPress");
			return BridgeResponse.Success(new
			{
				opened = true,
				state = DescribeConstructedFormatState(deckPickerTrayDisplay),
				method = "DeckPickerTrayDisplay.SwitchFormatButtonPress"
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private BridgeResponse ConstructedSwitchFormat(BridgeRequest request)
	{
		try
		{
			object deckPickerTrayDisplay = GetDeckPickerTrayDisplay();
			if (deckPickerTrayDisplay == null)
			{
				return BridgeResponse.Failure("DeckPickerTrayDisplay is not available.");
			}
			string text = GetString(request.ArgumentsJson, "format");
			int num = GetInt(request.ArgumentsJson, "visualsFormatType", ToConstructedVisualsFormatType(text));
			if (!IsSupportedConstructedVisualsFormatType(num))
			{
				return BridgeResponse.Failure("Unsupported constructed format: " + text);
			}
			if (!TryInvokeBool(deckPickerTrayDisplay, "CanSwitchFormats"))
			{
				return BridgeResponse.Failure("DeckPickerTrayDisplay.CanSwitchFormats() returned false.");
			}
			object before = DescribeConstructedFormatState(deckPickerTrayDisplay);
			if (!TryInvokeVisualsFormatTypeArg(deckPickerTrayDisplay, "SwitchFormatTypeAndRankedPlayMode", num, out var methodNameWithSignature, out var error))
			{
				return BridgeResponse.Failure(error);
			}
			return BridgeResponse.Success(new
			{
				switched = true,
				requestedFormat = text,
				requestedVisualsFormatType = num,
				before = before,
				after = DescribeConstructedFormatState(deckPickerTrayDisplay),
				method = methodNameWithSignature
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private BridgeResponse ConstructedDecks(BridgeRequest request)
	{
		try
		{
			ConstructedDeckScanResult constructedDeckScanResult = ScanConstructedDeckPages("", null, stopAtMatch: false, restoreOriginalPage: true);
			object[] array = constructedDeckScanResult.Decks.Select(ToConstructedDeckPayload).ToArray();
			return BridgeResponse.Success(new
			{
				count = array.Length,
				decks = array,
				state = DescribeConstructedFormatState(GetDeckPickerTrayDisplay()),
				pagination = ToConstructedDeckPaginationPayload(constructedDeckScanResult)
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private BridgeResponse ConstructedDeckCatalog()
	{
		try
		{
			if (IsActiveGameInProgress())
			{
				return BridgeResponse.Failure("Deck catalog validation is deferred until after gameplay.");
			}
			OwnedConstructedDeckCandidate[] array = ReadOwnedConstructedDeckCatalog().ToArray();
			OwnedConstructedDeckCandidate[] array2 = array;
			foreach (OwnedConstructedDeckCandidate ownedConstructedDeckCandidate in array2)
			{
				if (_deckCompletions.TryGetValue(ownedConstructedDeckCandidate.Id, out var value) && (!value.CallbackFinished || !string.IsNullOrEmpty(value.Error)))
				{
					ownedConstructedDeckCandidate.Availability.IsPlayable = false;
					ownedConstructedDeckCandidate.Availability.CanAutoComplete = false;
					ownedConstructedDeckCandidate.Availability.Reason = (string.IsNullOrEmpty(value.Error) ? "等待游戏完成自动补全" : value.Error);
				}
			}
			if (array.Length != 0)
			{
				return BridgeResponse.Success(new
				{
					count = array.Length,
					decks = array.Select((OwnedConstructedDeckCandidate deck) => new
					{
						deckId = deck.Id,
						name = deck.Name,
						normalizedName = deck.Name,
						pageIndex = -1,
						formatType = deck.FormatType,
						formatSource = deck.FormatSource,
						isStandard = deck.IsStandard,
						isWild = deck.IsWild,
						isPlayable = deck.Availability.IsPlayable,
						cardCount = deck.Availability.CardCount,
						minimumCardCount = deck.Availability.MinimumCardCount,
						maximumCardCount = deck.Availability.MaximumCardCount,
						usableCardCount = deck.Availability.UsableCardCount,
						canAutoComplete = deck.Availability.CanAutoComplete,
						unavailableReason = deck.Availability.Reason
					}).ToArray(),
					pageCount = 0,
					method = "CollectionManager.GetDecks()+GetBaseDeck(long)"
				});
			}
			object deckPickerTrayDisplay = GetDeckPickerTrayDisplay();
			if (deckPickerTrayDisplay == null)
			{
				return BridgeResponse.Failure("Owned constructed decks are not ready and DeckPickerTrayDisplay is not available.");
			}
			if (!(ReadMember(deckPickerTrayDisplay, "m_customPages") is IEnumerable enumerable))
			{
				return BridgeResponse.Failure("DeckPickerTrayDisplay.m_customPages is not ready.");
			}
			List<object> list = new List<object>();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int num = -1;
			foreach (object item in enumerable)
			{
				num++;
				if (item == null || !(ReadMember(item, "m_collectionDecks") is IEnumerable enumerable2))
				{
					continue;
				}
				foreach (object item2 in enumerable2)
				{
					string text = NormalizeConstructedDeckName(ReadMember(item2, "Name")?.ToString() ?? InvokeNoArg(item2, "get_Name")?.ToString() ?? "");
					string text2 = ReadMember(item2, "FormatType")?.ToString() ?? InvokeNoArg(item2, "get_FormatType")?.ToString() ?? "";
					bool flag = text2.IndexOf("STANDARD", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(text2, "2", StringComparison.Ordinal);
					bool flag2 = text2.IndexOf("WILD", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(text2, "1", StringComparison.Ordinal);
					string formatSource = "FormatType";
					if (!flag && !flag2 && InvokeNoArg(item2, "get_IsStandardDeck") is bool flag3)
					{
						flag = flag3;
						flag2 = !flag3;
						formatSource = "IsStandardDeck";
					}
					if (!string.IsNullOrWhiteSpace(text) && hashSet.Add(text + "\u001f" + text2))
					{
						ConstructedDeckAvailability constructedDeckAvailability = ReadConstructedDeckAvailability(item2);
						list.Add(new
						{
							name = text,
							normalizedName = text,
							pageIndex = num,
							formatType = text2,
							formatSource = formatSource,
							isStandard = flag,
							isWild = flag2,
							isPlayable = constructedDeckAvailability.IsPlayable,
							cardCount = constructedDeckAvailability.CardCount,
							minimumCardCount = constructedDeckAvailability.MinimumCardCount,
							maximumCardCount = constructedDeckAvailability.MaximumCardCount,
							unavailableReason = constructedDeckAvailability.Reason
						});
					}
				}
			}
			return BridgeResponse.Success(new
			{
				count = list.Count,
				decks = list.ToArray(),
				pageCount = num + 1,
				method = "DeckPickerTrayDisplay.m_customPages[].m_collectionDecks[]"
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private static IEnumerable<OwnedConstructedDeckCandidate> ReadOwnedConstructedDeckCatalog()
	{
		object manager = InvokeStaticNoArg("CollectionManager", "Get");
		if (manager == null || !(InvokeNoArg(manager, "GetDecks") is IEnumerable enumerable))
		{
			yield break;
		}
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (object item2 in enumerable)
		{
			object obj = ((item2 is DictionaryEntry dictionaryEntry) ? dictionaryEntry.Value : ReadMember(item2, "Value"));
			if (obj == null)
			{
				continue;
			}
			object obj2 = InvokeNoArg(obj, "get_IsConstructedDeck");
			if (!(obj2 is bool) || !(bool)obj2)
			{
				continue;
			}
			long num = ReadMemberAsLong(obj, "ID", 0L);
			object instance = ((num > 0) ? InvokeInt64Arg(manager, "GetBaseDeck", num) : null) ?? obj;
			string text = NormalizeConstructedDeckName(InvokeNoArg(instance, "get_Name")?.ToString() ?? ReadMember(instance, "Name")?.ToString() ?? InvokeNoArg(obj, "get_Name")?.ToString() ?? "");
			if (!string.IsNullOrWhiteSpace(text))
			{
				string text2 = ReadMember(instance, "FormatType")?.ToString() ?? InvokeNoArg(instance, "get_FormatType")?.ToString() ?? ReadMember(obj, "FormatType")?.ToString() ?? InvokeNoArg(obj, "get_FormatType")?.ToString() ?? "";
				bool flag = text2.IndexOf("STANDARD", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(text2, "2", StringComparison.Ordinal);
				bool flag2 = text2.IndexOf("WILD", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(text2, "1", StringComparison.Ordinal);
				string formatSource = "FormatType";
				if (!flag && !flag2 && (InvokeNoArg(instance, "get_IsStandardDeck") ?? InvokeNoArg(obj, "get_IsStandardDeck")) is bool flag3)
				{
					flag = flag3;
					flag2 = !flag3;
					formatSource = "IsStandardDeck";
				}
				string text3 = text;
				string text4;
				if (flag)
				{
					text4 = "standard";
				}
				else
				{
					text4 = (flag2 ? "wild" : text2);
				}
				string item = text3 + "\u001f" + text4;
				if (seen.Add(item))
				{
					yield return new OwnedConstructedDeckCandidate
					{
						Id = num,
						Deck = obj,
						OriginalFormat = InvokeNoArg(instance, "get_FormatType"),
						Name = text,
						FormatType = text2,
						FormatSource = formatSource,
						IsStandard = flag,
						IsWild = flag2,
						Availability = ReadConstructedDeckAvailability(obj, InvokeNoArg(instance, "get_FormatType"))
					};
				}
			}
		}
	}


	private BridgeResponse ConstructedSelectDeck(BridgeRequest request)
	{
		try
		{
			string requestedName = NormalizeConstructedDeckName(GetString(request.ArgumentsJson, "deckName"));
			int? nullableInt = GetNullableInt(request.ArgumentsJson, "deckIndex");
			int? requestedIndex = ((nullableInt.HasValue && nullableInt.Value >= 0) ? nullableInt : ((int?)null));
			ConstructedDeckScanResult constructedDeckScanResult = ScanConstructedDeckPages(requestedName, requestedIndex, stopAtMatch: true, restoreOriginalPage: false);
			ConstructedDeckCandidate target = constructedDeckScanResult.Target;
			if (target == null)
			{
				return BridgeResponse.Failure("Requested constructed deck was not found after scanning every available page.", new
				{
					requestedName = requestedName,
					requestedIndex = requestedIndex,
					count = constructedDeckScanResult.Decks.Count,
					decks = constructedDeckScanResult.Decks.Select(ToConstructedDeckPayload).ToArray(),
					pagination = ToConstructedDeckPaginationPayload(constructedDeckScanResult)
				});
			}
			string text = TrySelectConstructedDeckInternally(target) ?? TryInvokePegUiPressRelease(target.GameObject) ?? TryInvokePegUiClick(target.GameObject) ?? TryInvokeCommonClickMethod(target.GameObject);
			if (string.IsNullOrWhiteSpace(text))
			{
				SendCommonClickMessages(target.GameObject);
				text = "SendMessage click sequence";
			}
			object[] array = ReadConstructedDeckCandidates(target.PageIndex, target.PageOrdinal).Select(ToConstructedDeckPayload).ToArray();
			object obj = array.FirstOrDefault((object deck) => ReadMemberAsInt(deck, "instanceId", 0) == target.InstanceId);
			return BridgeResponse.Success(new
			{
				selected = (obj != null && ReadMemberAsBool(obj, "selected", defaultValue: false)),
				requestedName = requestedName,
				requestedIndex = requestedIndex,
				deck = ToConstructedDeckPayload(target),
				method = text,
				decks = array,
				pagination = ToConstructedDeckPaginationPayload(constructedDeckScanResult)
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private BridgeResponse ConstructedEndTurn()
	{
		try
		{
			object gameState = GetGameState();
			if (gameState == null)
			{
				return BridgeResponse.Failure("GameState.Get() returned null.");
			}
			if (!(InvokeNoArg(InvokeNoArg(gameState, "GetOptionsPacket"), "get_List") is IEnumerable optionList))
			{
				return BridgeResponse.Failure("GameState.GetOptionsPacket().List was not available.");
			}
			if (TryFindEndTurnOption(optionList, out var selectedIndex, out var selectedType, out var selectedTypeName, out var candidates))
			{
				if (!InvokeIntArg(gameState, "SetSelectedOption", selectedIndex))
				{
					return BridgeResponse.Failure("GameState.SetSelectedOption(int) not found.");
				}
				SendOptionWithProcessUserUi(gameState, 0, 0, holdSource: false, "constructedEndTurn");
				TryNotifyEndTurnButtonRequested();
				return BridgeResponse.Success(new
				{
					accepted = true,
					selectedIndex = selectedIndex,
					selectedType = selectedType,
					selectedTypeName = selectedTypeName,
					method = "GameState.SetSelectedOption(int)+SendOption()",
					candidates = candidates
				});
			}
			return BridgeResponse.Failure("No END_TURN option was available.");
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private BridgeResponse ConstructedTargetedAction(BridgeRequest request)
	{
		int num = GetInt(request.ArgumentsJson, "sourceEntityId", 0);
		int num2 = GetInt(request.ArgumentsJson, "targetEntityId", 0);
		string a = GetString(request.ArgumentsJson, "semanticAction") ?? "";
		if (num <= 0)
		{
			return BridgeResponse.Failure("Source entityId is required.");
		}
		if (num2 <= 0)
		{
			return BridgeResponse.Failure("Target entityId is required.");
		}
		return SubmitOptionTarget(num2, num, null, "No constructed option accepted target entity", null, string.Equals(a, "Attack", StringComparison.OrdinalIgnoreCase));
	}


	private BridgeResponse ConstructedOptionAction(BridgeRequest request)
	{
		int num = GetInt(request.ArgumentsJson, "sourceEntityId", 0);
		int num2 = GetInt(request.ArgumentsJson, "optionIndex", -1);
		int num3 = GetInt(request.ArgumentsJson, "targetEntityId", 0);
		List<int> list = GetIntArray(request.ArgumentsJson, "targetEntityIds") ?? new List<int>();
		int subOptionIndex = GetInt(request.ArgumentsJson, "subOptionIndex", -1);
		int requestedSubOptionEntityId = GetInt(request.ArgumentsJson, "subOptionEntityId", 0);
		string requestedSubOptionCardId = GetString(request.ArgumentsJson, "subOptionCardId") ?? "";
		int requestedSubOptionPosition = GetInt(request.ArgumentsJson, "subOptionPosition", 0);
		int num4 = GetInt(request.ArgumentsJson, "boardPosition", 0);
		string a = GetString(request.ArgumentsJson, "semanticAction") ?? "";
		if (num <= 0 || num2 < 0)
		{
			return BridgeResponse.Failure("Source entityId and optionIndex are required.");
		}
		object gameState = GetGameState();
		if (!(InvokeNoArg(InvokeNoArg(gameState, "GetOptionsPacket"), "get_List") is IEnumerable enumerable))
		{
			return BridgeResponse.Failure("GameState.GetOptionsPacket().List was not available.");
		}
		int num5 = 0;
		foreach (object item in enumerable)
		{
			if (num5++ != num2)
			{
				continue;
			}
			object obj = InvokeNoArg(item, "get_Main");
			int num6 = ReadIntMember(obj, "get_ID", -1);
			if (num6 != num)
			{
				return BridgeResponse.Failure($"Option {num2} belongs to entity {num6}, not {num}.");
			}
			object instance = obj;
			int subOptionEntityId = 0;
			string subOptionCardId = "";
			int subOptionPosition = 0;
			if (subOptionIndex >= 0 || requestedSubOptionEntityId > 0 || !string.IsNullOrWhiteSpace(requestedSubOptionCardId))
			{
				if (!(InvokeNoArg(item, "get_Subs") is IEnumerable enumerable2))
				{
					return BridgeResponse.Failure($"Option {num2} has no sub-options.");
				}
				List<ConstructedSubOptionCandidate> list2 = new List<ConstructedSubOptionCandidate>();
				int num7 = 0;
				foreach (object item2 in enumerable2)
				{
					int index = num7++;
					if (item2 != null)
					{
						int num8 = ReadIntMember(item2, "get_ID", 0);
						object obj2 = ResolveOptionMainEntity(gameState, item2, num8);
						list2.Add(new ConstructedSubOptionCandidate
						{
							Index = index,
							Part = item2,
							EntityId = num8,
							CardId = ResolveOptionMainCardId(item2, obj2),
							ZonePosition = ReadIntMember(obj2, "GetZonePosition", 0),
							IsValid = IsPlayErrorInfoValid(InvokeNoArg(item2, "get_PlayErrorInfo"))
						});
					}
				}
				ConstructedSubOptionCandidate[] array;
				if (string.IsNullOrWhiteSpace(requestedSubOptionCardId))
				{
					array = ((requestedSubOptionEntityId > 0) ? list2.Where((ConstructedSubOptionCandidate candidate) => candidate.EntityId == requestedSubOptionEntityId).ToArray() : list2.Where((ConstructedSubOptionCandidate candidate) => candidate.Index == subOptionIndex).ToArray());
				}
				else
				{
					array = list2.Where((ConstructedSubOptionCandidate candidate) => string.Equals(candidate.CardId, requestedSubOptionCardId, StringComparison.OrdinalIgnoreCase)).ToArray();
				}
				if (array.Length > 1 && requestedSubOptionPosition > 0)
				{
					array = array.Where((ConstructedSubOptionCandidate candidate) => candidate.ZonePosition == requestedSubOptionPosition).ToArray();
				}
				if (array.Length != 1)
				{
					return BridgeResponse.Failure($"Sub-option {subOptionIndex}/{requestedSubOptionEntityId}/{requestedSubOptionCardId} " + $"did not uniquely match option {num2}.");
				}
				ConstructedSubOptionCandidate constructedSubOptionCandidate = array[0];
				if (!constructedSubOptionCandidate.IsValid)
				{
					return BridgeResponse.Failure($"Sub-option {constructedSubOptionCandidate.Index}/{constructedSubOptionCandidate.CardId} is no longer valid for option {num2}.");
				}
				subOptionIndex = constructedSubOptionCandidate.Index;
				subOptionEntityId = constructedSubOptionCandidate.EntityId;
				subOptionCardId = constructedSubOptionCandidate.CardId;
				subOptionPosition = constructedSubOptionCandidate.ZonePosition;
				instance = constructedSubOptionCandidate.Part;
			}
			bool flag = list.Count > 1;
			if (!flag && num3 > 0 && !TryInvokeIntArgBool(instance, "IsValidTarget", num3))
			{
				return BridgeResponse.Failure($"Target entity {num3} is not valid for option {num2}.");
			}
			if (!InvokeIntArg(gameState, "SetSelectedOption", num2))
			{
				return BridgeResponse.Failure("GameState.SetSelectedOption(int) not found.");
			}
			if (subOptionIndex >= 0 && !InvokeIntArg(gameState, "SetSelectedSubOption", subOptionIndex))
			{
				return BridgeResponse.Failure("GameState.SetSelectedSubOption(int) not found.");
			}
			if (!flag && num3 > 0 && !InvokeIntArg(gameState, "SetSelectedOptionTarget", num3))
			{
				return BridgeResponse.Failure("GameState.SetSelectedOptionTarget(int) not found.");
			}
			if (num4 > 0 && !InvokeIntArg(gameState, "SetSelectedOptionPosition", num4))
			{
				return BridgeResponse.Failure("GameState.SetSelectedOptionPosition(int) not found.");
			}
			object obj3 = ResolveOptionMainEntity(gameState, obj, num6);
			bool trackAttackCandidate = string.Equals(a, "Attack", StringComparison.OrdinalIgnoreCase) && IsDirectConstructedAttackOption(gameState, item, obj, obj3, num3, subOptionIndex, num4);
			SendOptionWithProcessUserUi(gameState, num, (!flag) ? num3 : 0, IsHandEntity(obj3), "constructedOptionAction", trackAttackCandidate);
			return BridgeResponse.Success(new
			{
				accepted = true,
				submitted = true,
				sourceEntityId = num,
				targetEntityId = ((!flag) ? num3 : 0),
				targetEntityIds = list,
				awaitingEntityChoices = flag,
				optionIndex = num2,
				subOptionIndex = subOptionIndex,
				subOptionEntityId = subOptionEntityId,
				subOptionCardId = subOptionCardId,
				subOptionPosition = subOptionPosition,
				boardPosition = num4,
				type = ReadIntMember(item, "get_Type", int.MinValue),
				method = (flag ? "GameState.SetSelectedOption(+SubOption+Position)+SendOption(); await EntityChoices" : "GameState.SetSelectedOption(+SubOption+Target+Position)+SendOption()")
			});
		}
		return BridgeResponse.Failure($"Option index {num2} no longer exists.");
	}


	private BridgeResponse ConstructedCancelInput()
	{
		try
		{
			object inputManager = GetInputManager();
			if (inputManager == null)
			{
				return BridgeResponse.Failure("InputManager.Get() returned null.");
			}
			object before = DescribeConstructedInputState(inputManager);
			MethodInfo methodInfo = inputManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, "CancelOption", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
			});
			if (methodInfo == null)
			{
				return BridgeResponse.Failure("InputManager.CancelOption(bool) not found.");
			}
			object obj = methodInfo.Invoke(inputManager, new object[1] { false });
			bool flag = default;
			int num;
			if (obj is bool)
			{
				flag = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			bool cancelled = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
			return BridgeResponse.Success(new
			{
				accepted = true,
				cancelled = cancelled,
				method = inputManager.GetType().FullName + ".CancelOption(bool)",
				before = before,
				after = DescribeConstructedInputState(inputManager)
			});
		}
		catch (Exception ex)
		{
			return BridgeResponse.Failure(FlattenInvocationException(ex));
		}
	}


	private static object DescribeConstructedInputState(object inputManager)
	{
		object gameState = GetGameState();
		return new
		{
			inputEnabled = TryInvokeBool(inputManager, "IsInputEnabled"),
			heldCard = (InvokeNoArg(inputManager, "GetHeldCard") != null),
			mainOptionMode = TryInvokeBool(gameState, "IsInMainOptionMode"),
			targetMode = TryInvokeBool(gameState, "IsInTargetMode"),
			subOptionMode = TryInvokeBool(gameState, "IsInSubOptionMode"),
			reverseTargetMode = TryInvokeBool(gameState, "IsInReverseTargetMode")
		};
	}


	private BridgeResponse ReadFriendlyChoiceCards()
	{
		Component val = FindSingletonComponent("ChoiceCardMgr");
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Success(new
			{
				shown = false,
				count = 0,
				objects = Array.Empty<object>()
			});
		}
		bool shown = IsFriendlyChoicesShown(val) || TryInvokeBool(val, "HasFriendlyChoices");
		GameObject[] source = ReadChoiceCardObjects(val).ToArray();
		int visibleCount = Enumerable.Count(source, IsChoiceCardVisuallyShown);
		object[] array = (from gameObject in source
			select TryDescribeObject(gameObject, includeComponents: false) into item
			where item != null
			select item).ToArray();
		return BridgeResponse.Success(new
		{
			shown = shown,
			isMagicItemDiscover = TryInvokeBool(val, "IsFriendlyMagicItemDiscover"),
			isShopChoice = TryInvokeBool(val, "IsFriendlyShopChoice"),
			count = array.Length,
			visibleCount = visibleCount,
			objects = array
		});
	}


	private BridgeResponse CleanupFriendlyChoiceVisuals()
	{
		Component val = FindSingletonComponent("ChoiceCardMgr");
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Success(new
			{
				cleaned = 0,
				visibleBefore = 0,
				visibleAfter = 0
			});
		}
		GameObject[] array = ReadChoiceCardObjects(val).ToArray();
		int visibleBefore = Enumerable.Count(array, IsChoiceCardVisuallyShown);
		int num = 0;
		GameObject[] array2 = array;
		foreach (GameObject val2 in array2)
		{
			if ((Object)(object)val2 == (Object)null || !IsChoiceCardVisuallyShown(val2))
			{
				continue;
			}
			Component val3 = FindCardComponent(val2);
			if (!((Object)(object)val3 == (Object)null))
			{
				bool flag = TryInvokeSingleArgMethod(val, "HideChoiceCard", val3, out var methodName, out var error);
				bool flag2 = TryInvokeZeroArgMethod(val3, "HideCard", out error, out methodName);
				object obj = InvokeNoArg(val3, "GetActor");
				bool flag3 = obj != null && TryInvokeZeroArgMethod(obj, "Hide", out methodName, out error);
				if (flag | flag2 | flag3)
				{
					num++;
				}
			}
		}
		int visibleAfter = Enumerable.Count(array, IsChoiceCardVisuallyShown);
		return BridgeResponse.Success(new
		{
			cleaned = num,
			visibleBefore = visibleBefore,
			visibleAfter = visibleAfter
		});
	}


	private bool IsChoiceCardVisuallyShown(GameObject gameObject)
	{
		if ((Object)(object)gameObject == (Object)null || !gameObject.activeInHierarchy)
		{
			return false;
		}
		Component val = FindCardComponent(gameObject);
		if ((Object)(object)val == (Object)null)
		{
			return false;
		}
		object obj = InvokeNoArg(val, "GetActor");
		if (!TryInvokeBool(val, "IsShown") && !ReadMemberAsBool(val, "m_shown", defaultValue: false))
		{
			if (obj != null)
			{
				return TryInvokeBool(obj, "IsShown");
			}
			return false;
		}
		return true;
	}


	private BridgeResponse SelectFriendlyChoice(BridgeRequest request)
	{
		bool flag = string.Equals(GetString(request.ArgumentsJson, "mode"), "BuiltInDarkGift", StringComparison.OrdinalIgnoreCase);
		bool flag2 = string.Equals(GetString(request.ArgumentsJson, "mode"), "Jail319Reroll", StringComparison.OrdinalIgnoreCase);
		object gameState = GetGameState();
		int[] targetChoiceEntityIds = ReadFriendlyTargetChoiceEntityIds(gameState, out var choiceId, out var countMin, out var countMax);
		int[] array = ReadFriendlyKerriganChoiceEntityIds(gameState, out var choiceId2, out var _, out var _);
		Component manager = FindSingletonComponent("ChoiceCardMgr");
		bool flag3 = (Object)(object)manager != (Object)null && (IsFriendlyChoicesShown(manager) || TryInvokeBool(manager, "HasFriendlyChoices"));
		if (!flag3 && targetChoiceEntityIds.Length == 0 && array.Length == 0)
		{
			return BridgeResponse.Failure("No current friendly card or entity target choice is pending.");
		}
		if (flag2)
		{
			return SelectJail319Reroll();
		}
		List<int> list = GetIntArray(request.ArgumentsJson, "targetEntityIds") ?? new List<int>();
		if (list.Count > 1)
		{
			if (targetChoiceEntityIds.Length == 0)
			{
				return BridgeResponse.Failure("A multi-entity response requires a current friendly entity target choice.");
			}
			if (list.Count < countMin || (countMax > 0 && list.Count > countMax))
			{
				return BridgeResponse.Failure($"Friendly logical choice {choiceId} requires {countMin} to {countMax} entities, not {list.Count}.");
			}
			int num = list.FirstOrDefault((int entityId) => !Enumerable.Contains(targetChoiceEntityIds, entityId));
			if (num > 0)
			{
				return BridgeResponse.Failure($"Entity target {num} is not in friendly logical choice {choiceId}.");
			}
			object[] array2 = list.Select((int entityId) => InvokeIntArgObject(gameState, "GetEntity", entityId) ?? InvokeIntArgObject(gameState, "GetEntityByID", entityId)).ToArray();
			if (array2.Any((object entity) => entity == null))
			{
				return BridgeResponse.Failure($"One or more entities from friendly logical choice {choiceId} were not found.");
			}
			if (!TrySubmitExactFriendlyChoices(gameState, array2, out var methodName))
			{
				return BridgeResponse.Failure($"Unable to submit {list.Count} entity targets for friendly logical choice {choiceId}.");
			}
			return BridgeResponse.Success(new
			{
				optionIndex = -1,
				choiceCount = targetChoiceEntityIds.Length,
				targetEntityId = 0,
				targetEntityIds = list,
				choiceId = choiceId,
				accepted = true,
				isEntityTargetChoice = true,
				method = gameState.GetType().FullName + "." + methodName
			});
		}
		IEnumerable<GameObject> source;
		if (!flag3)
		{
			IEnumerable<GameObject> enumerable = Array.Empty<GameObject>();
			source = enumerable;
		}
		else
		{
			source = ReadChoiceCardObjects(manager);
		}
		var array3 = (from item in source.Select((GameObject gameObject) =>
			{
				Component val = FindCardComponent(gameObject);
				object obj3 = (((Object)(object)val == (Object)null) ? null : InvokeNoArg(val, "GetEntity"));
				return new
				{
					GameObject = gameObject,
					Card = val,
					Entity = obj3,
					ZonePosition = ReadIntMember(obj3, "GetZonePosition", int.MaxValue),
					EntityId = ReadIntMember(obj3, "GetEntityId", int.MaxValue)
				};
			})
			where (Object)(object)item.Card != (Object)null && item.Entity != null
			orderby item.ZonePosition, item.EntityId
			select item).ToArray();
		bool flag4 = TryInvokeBool(manager, "HasSubOption");
		object obj = ((flag3 && !flag4) ? InvokeNoArg(manager, "GetFriendlyChoiceState") : null);
		object instance = ((obj == null) ? null : InvokeIntArgObject(gameState, "GetEntity", ReadMemberAsInt(obj, "m_sourceEntityId", 0)));
		bool flag5 = !flag4 && FriendlyChoiceReadiness.IsDarkGift(InvokeNoArg(instance, "GetCardId")?.ToString() ?? "", array3.Any(item => ReadEntityTagInt(item.Entity, "DARK_GIFT_ENTITY") > 0 || ReadEntityTagBool(item.Entity, "HAS_DARK_GIFT")));
		if (flag5)
		{
			object instance2 = InvokeNoArg(gameState, "GetFriendlyEntityChoices");
			int num2 = ReadIntMember(instance2, "get_ID", 0);
			int[] array4 = ReadChoiceEntityIds(InvokeNoArg(instance2, "get_Entities") as IEnumerable).Concat(ReadChoiceEntityIds(InvokeNoArg(instance2, "get_UnchoosableEntities") as IEnumerable)).Distinct().Take(32)
				.ToArray();
			int[] array5 = (from item in array3
				where TryInvokeSingleArgNullableBool(manager, "IsChoiceCardReady", item.Card) == true && IsChoiceCardVisuallyShown(item.GameObject)
				select item.EntityId).ToArray();
			if (!FriendlyChoiceReadiness.CanSelect(num2, ReadMemberAsInt(obj, "m_choiceID", 0), ReadMemberAsBool(obj, "m_waitingToStart", defaultValue: true), ReadMemberAsBool(obj, "m_hasBeenRevealed", defaultValue: false), ReadMemberAsBool(obj, "m_hasBeenConcealed", defaultValue: true), array4, array5))
			{
				return BridgeResponse.Success(new
				{
					accepted = false,
					selectionDeferred = true,
					choiceId = num2,
					expectedCount = array4.Length,
					readyCount = array5.Length,
					reason = "Waiting for every current choice card and its reveal animation."
				});
			}
		}
		if (array3.Length == 0)
		{
			int[] array6 = ((targetChoiceEntityIds.Length != 0) ? targetChoiceEntityIds : array);
			int num3 = ((targetChoiceEntityIds.Length != 0) ? choiceId : choiceId2);
			bool isKerriganTurnStartChoice = array.Length != 0;
			int num4 = GetInt(request.ArgumentsJson, "targetEntityId", 0);
			int num5 = GetInt(request.ArgumentsJson, "optionIndex", 0);
			int num6 = ((num4 > 0) ? num4 : ((num5 >= 0 && num5 < array6.Length) ? array6[num5] : 0));
			if (num6 <= 0 || !Enumerable.Contains(array6, num6))
			{
				return BridgeResponse.Failure($"Entity target {num6} is not in friendly logical choice {num3}.");
			}
			object obj2 = InvokeIntArgObject(gameState, "GetEntity", num6) ?? InvokeIntArgObject(gameState, "GetEntityByID", num6);
			if (obj2 == null)
			{
				return BridgeResponse.Failure($"Entity target {num6} from friendly logical choice {num3} was not found.");
			}
			if (!TrySubmitExactFriendlyChoice(gameState, obj2, out var methodName2))
			{
				return BridgeResponse.Failure($"Unable to submit entity target {num6} for friendly logical choice {num3}.");
			}
			return BridgeResponse.Success(new
			{
				optionIndex = Array.IndexOf(array6, num6),
				choiceCount = array6.Length,
				targetEntityId = num6,
				choiceId = num3,
				accepted = true,
				isEntityTargetChoice = (targetChoiceEntityIds.Length != 0),
				isKerriganTurnStartChoice = isKerriganTurnStartChoice,
				method = gameState.GetType().FullName + "." + methodName2
			});
		}
		int optionIndex = GetInt(request.ArgumentsJson, "optionIndex", 0);
		if (optionIndex < 0 || optionIndex >= array3.Length)
		{
			return BridgeResponse.Failure($"Choice index {optionIndex} is outside the current choice count {array3.Length}.");
		}
		var anon = array3[optionIndex];
		int num7 = GetInt(request.ArgumentsJson, "expectedDarkGiftEntityId", GetInt(request.ArgumentsJson, "targetEntityId", 0));
		if (flag5 && num7 > 0 && anon.EntityId != num7)
		{
			return BridgeResponse.Success(new
			{
				accepted = false,
				selectionDeferred = true,
				reason = "The recommended choice no longer matches the current visible index."
			});
		}
		GameObject[] array7 = (from item in array3.Where((_, index) => index != optionIndex)
			select item.GameObject into gameObject
			where (Object)(object)gameObject != (Object)null
			select gameObject).ToArray();
		int[] array8 = (from item in array3.Where((_, index) => index != optionIndex)
			select item.EntityId).ToArray();
		if (flag5)
		{
			_lastDarkGiftChoiceId = ReadMemberAsInt(obj, "m_choiceID", 0);
			_lastDarkGiftSelectedId = anon.EntityId;
			_lastDarkGiftReadyCount = array3.Length;
			_lastDarkGiftSourceCardId = InvokeNoArg(instance, "GetCardId")?.ToString() ?? "";
			_lastDarkGiftUnselectedObjects = array7;
			_lastDarkGiftUnselectedIds = array8;
		}
		object inputManager = GetInputManager();
		if (flag4)
		{
			if (!TryHandleClickOnSubOption(inputManager, anon.Entity, out var methodName3))
			{
				return BridgeResponse.Failure("InputManager.HandleClickOnSubOption(Entity, bool, bool) not found.");
			}
			if (array7.Length != 0)
			{
				ScheduleFriendlyChoiceCleanup(flag, array7, array8);
			}
			return BridgeResponse.Success(new
			{
				optionIndex = optionIndex,
				choiceCount = array3.Length,
				target = Describe(anon.GameObject, includeComponents: false),
				accepted = true,
				isSubOption = true,
				staleChoiceCleanupScheduled = array7.Length,
				method = inputManager.GetType().FullName + "." + methodName3
			});
		}
		bool? flag6 = TryInvokeSingleArgNullableBool(manager, "IsCardReady", anon.Card);
		bool? flag7 = TryInvokeSingleArgNullableBool(manager, "IsEntityReady", anon.Entity);
		if (flag6 == false || flag7 == false)
		{
			return BridgeResponse.Failure("Choice card is not ready: cardReady=" + FormatNullableBool(flag6) + ", entityReady=" + FormatNullableBool(flag7) + ".");
		}
		if (flag && TryClearFriendlyChosenEntities(gameState, out var methodName4) && TryHandleClickOnChoice(inputManager, anon.Entity, out var methodName5))
		{
			if (array7.Length != 0)
			{
				ScheduleFriendlyChoiceCleanup(flag, array7, array8);
			}
			return BridgeResponse.Success(new
			{
				optionIndex = optionIndex,
				choiceCount = array3.Length,
				target = Describe(anon.GameObject, includeComponents: false),
				accepted = true,
				staleChoiceCleanupScheduled = array7.Length,
				method = gameState.GetType().FullName + "." + methodName4 + " + " + inputManager.GetType().FullName + "." + methodName5
			});
		}
		if (TrySubmitExactFriendlyChoice(gameState, anon.Entity, out var methodName6))
		{
			if (array7.Length != 0)
			{
				ScheduleFriendlyChoiceCleanup(flag, array7, array8);
			}
			return BridgeResponse.Success(new
			{
				optionIndex = optionIndex,
				choiceCount = array3.Length,
				target = Describe(anon.GameObject, includeComponents: false),
				accepted = true,
				staleChoiceCleanupScheduled = array7.Length,
				method = gameState.GetType().FullName + "." + methodName6
			});
		}
		if (TryHandleClickOnChoice(inputManager, anon.Entity, out var methodName7))
		{
			if (array7.Length != 0)
			{
				ScheduleFriendlyChoiceCleanup(flag, array7, array8);
			}
			return BridgeResponse.Success(new
			{
				optionIndex = optionIndex,
				choiceCount = array3.Length,
				target = Describe(anon.GameObject, includeComponents: false),
				accepted = true,
				staleChoiceCleanupScheduled = array7.Length,
				method = inputManager.GetType().FullName + "." + methodName7
			});
		}
		bool? flag8 = TryDoNetworkResponse(inputManager, anon.Entity, checkValidInput: true, wantDeckActionOption: false, wantDisguisedOption: false, out var methodName8);
		if ((!flag8) ?? true)
		{
			flag8 = TryDoNetworkResponse(inputManager, anon.Entity, checkValidInput: false, wantDeckActionOption: false, wantDisguisedOption: false, out methodName8);
		}
		if (flag8 ?? false)
		{
			if (array7.Length != 0)
			{
				ScheduleFriendlyChoiceCleanup(flag, array7, array8);
			}
			return BridgeResponse.Success(new
			{
				optionIndex = optionIndex,
				choiceCount = array3.Length,
				target = Describe(anon.GameObject, includeComponents: false),
				accepted = true,
				staleChoiceCleanupScheduled = array7.Length,
				method = inputManager.GetType().FullName + "." + methodName8
			});
		}
		string text = TryInvokeCommonClickMethod(anon.GameObject);
		SendCommonClickMessages(anon.GameObject);
		return BridgeResponse.Success(new
		{
			optionIndex = optionIndex,
			choiceCount = array3.Length,
			target = Describe(anon.GameObject, includeComponents: false),
			accepted = false,
			method = (text ?? "SendMessage click sequence")
		});
	}


	private static BridgeResponse SelectJail319Reroll()
	{
		object obj = InvokeStaticNoArg("RerollUIManager", "Get");
		if (obj == null)
		{
			return BridgeResponse.Failure("The current choice does not expose a reroll control.");
		}
		string text = InvokeNoArg(ReadMember(obj, "m_rerollEntity"), "GetCardId")?.ToString() ?? "";
		if (!string.Equals(text, "JAIL_319t", StringComparison.OrdinalIgnoreCase))
		{
			return BridgeResponse.Failure("The current reroll entity is '" + text + "', not the Jail 319 reroll token.");
		}
		MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo method) => string.Equals(method.Name, "OnButtonUp", StringComparison.Ordinal) && method.GetParameters().Length == 1 && !method.GetParameters()[0].ParameterType.IsValueType);
		if (methodInfo == null)
		{
			return BridgeResponse.Failure("The Jail 319 reroll control has no compatible release handler.");
		}
		methodInfo.Invoke(obj, new object[1]);
		return BridgeResponse.Success(new
		{
			optionIndex = 3,
			choiceCount = 4,
			cardId = text,
			accepted = true,
			isJail319Reroll = true,
			method = obj.GetType().FullName + "." + methodInfo.Name
		});
	}


	private void ScheduleFriendlyChoiceCleanup(bool useBuiltInDarkGiftChoiceFlow, GameObject[] choiceObjects, int[] expectedEntityIds)
	{
		((MonoBehaviour)this).StartCoroutine(useBuiltInDarkGiftChoiceFlow ? ConcealBuiltInDarkGiftStaleChoicesAfterResponse(choiceObjects, expectedEntityIds) : ConcealLegacyStaleFriendlyChoicesAfterResponse(choiceObjects));
	}


	private IEnumerator ConcealLegacyStaleFriendlyChoicesAfterResponse(GameObject[] choiceObjects)
	{
		yield return (object)new WaitForSeconds(1f);
		Component manager = null;
		for (int attempt = 0; attempt < 10; attempt++)
		{
			manager = FindSingletonComponent("ChoiceCardMgr");
			if ((Object)(object)manager == (Object)null || (!IsFriendlyChoicesShown(manager) && !TryInvokeBool(manager, "HasFriendlyChoices")))
			{
				break;
			}
			yield return (object)new WaitForSeconds(0.25f);
		}
		if ((Object)(object)manager != (Object)null && (IsFriendlyChoicesShown(manager) || TryInvokeBool(manager, "HasFriendlyChoices")))
		{
			yield break;
		}
		List<object> pendingCards = new List<object>();
		int nativeHideRequests = 0;
		string methodName;
		string error;
		foreach (GameObject val in choiceObjects)
		{
			if ((Object)(object)val == (Object)null)
			{
				continue;
			}
			try
			{
				Component val2 = FindCardComponent(val);
				object obj = (((Object)(object)val2 == (Object)null) ? null : InvokeNoArg(val2, "GetEntity"));
				string a = ((obj == null) ? "" : (InvokeNoArg(obj, "GetZone")?.ToString() ?? ""));
				if (!((Object)(object)val2 == (Object)null) && string.Equals(a, "SETASIDE", StringComparison.OrdinalIgnoreCase))
				{
					pendingCards.Add(val2);
					if ((Object)(object)manager != (Object)null && TryInvokeSingleArgMethod(manager, "HideChoiceCard", val2, out methodName, out error))
					{
						nativeHideRequests++;
					}
				}
			}
			catch
			{
			}
		}
		yield return (object)new WaitForSeconds(0.75f);
		int num = 0;
		foreach (object item in pendingCards)
		{
			try
			{
				object obj3 = InvokeNoArg(item, "GetActor");
				bool num2 = TryInvokeBool(item, "IsShown") || ReadMemberAsBool(item, "m_shown", defaultValue: false);
				bool flag = obj3 != null && TryInvokeBool(obj3, "IsShown");
				if (num2 || flag)
				{
					bool flag2 = TryInvokeZeroArgMethod(item, "HideCard", out error, out methodName);
					bool flag3 = obj3 != null && TryInvokeZeroArgMethod(obj3, "Hide", out methodName, out error);
					if (flag2 | flag3)
					{
						num++;
					}
				}
			}
			catch
			{
			}
		}
		if (pendingCards.Count > 0)
		{
			Logger.LogInfo($"Friendly choice cleanup completed for {pendingCards.Count} stale card(s): " + $"native={nativeHideRequests}, forced={num}.");
		}
	}


	private IEnumerator ConcealBuiltInDarkGiftStaleChoicesAfterResponse(GameObject[] choiceObjects, int[] expectedEntityIds)
	{
		yield return (object)new WaitForSeconds(1f);
		Component manager = FindSingletonComponent("ChoiceCardMgr");
		List<object> pendingCards = new List<object>();
		int nativeHideRequests = 0;
		string methodName;
		string error;
		for (int i = 0; i < choiceObjects.Length; i++)
		{
			GameObject val = choiceObjects[i];
			if ((Object)(object)val == (Object)null)
			{
				continue;
			}
			try
			{
				Component val2 = FindCardComponent(val);
				object obj = (((Object)(object)val2 == (Object)null) ? null : InvokeNoArg(val2, "GetEntity"));
				int num = ReadIntMember(obj, "GetEntityId", 0);
				string a = ((obj == null) ? "" : (InvokeNoArg(obj, "GetZone")?.ToString() ?? ""));
				if (!((Object)(object)val2 == (Object)null) && i < expectedEntityIds.Length && num == expectedEntityIds[i] && string.Equals(a, "SETASIDE", StringComparison.OrdinalIgnoreCase))
				{
					pendingCards.Add(val2);
					if ((Object)(object)manager != (Object)null && TryInvokeSingleArgMethod(manager, "HideChoiceCard", val2, out methodName, out error))
					{
						nativeHideRequests++;
					}
				}
			}
			catch
			{
			}
		}
		yield return (object)new WaitForSeconds(0.75f);
		int num2 = 0;
		foreach (object item in pendingCards)
		{
			try
			{
				object obj3 = InvokeNoArg(item, "GetActor");
				bool num3 = TryInvokeBool(item, "IsShown") || ReadMemberAsBool(item, "m_shown", defaultValue: false);
				bool flag = obj3 != null && TryInvokeBool(obj3, "IsShown");
				if (num3 || flag)
				{
					bool flag2 = TryInvokeZeroArgMethod(item, "HideCard", out error, out methodName);
					bool flag3 = obj3 != null && TryInvokeZeroArgMethod(obj3, "Hide", out methodName, out error);
					if (flag2 | flag3)
					{
						num2++;
					}
				}
			}
			catch
			{
			}
		}
		bool flag4 = false;
		if ((Object)(object)manager != (Object)null && !ReadChoiceCardObjects(manager).Any() && !TryInvokeBool(manager, "HasSubOption"))
		{
			object gameState = GetGameState();
			int[] array = ReadFriendlyTargetChoiceEntityIds(gameState, out var _, out var _, out var _);
			int num4 = ReadIntMember(gameState, "GetFriendlyPlayerId", 0);
			if (array.Length == 0 && num4 > 0)
			{
				TryInvokeSingleArgMethod(manager, "OnFinishedConcealChoices", num4, out error, out methodName);
				TryInvokeZeroArgMethod(manager, "HideChoiceUI", out methodName, out error);
				FieldInfo field = ((object)manager).GetType().GetField("m_friendlyChoicesShown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field?.FieldType == typeof(bool))
				{
					field.SetValue(manager, false);
					flag4 = true;
				}
			}
		}
		Logger.LogInfo($"Built-in Dark Gift choice cleanup completed for {pendingCards.Count} stale card(s): " + $"native={nativeHideRequests}, forced={num2}, " + $"managerFinalized={flag4}.");
	}


	private BridgeResponse InvokeMethodOnObject(BridgeRequest request)
	{
		Selector selector = Selector.From(ExtractObject(request.ArgumentsJson, "selector"));
		string componentContains = GetString(request.ArgumentsJson, "componentContains");
		string method = GetString(request.ArgumentsJson, "method");
		if (string.IsNullOrWhiteSpace(method))
		{
			return BridgeResponse.Failure("method is required.");
		}
		GameObject val = ResolveObject(selector);
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("Object not found.");
		}
		Component val2 = (from item in val.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).FirstOrDefault((Component item) => string.IsNullOrWhiteSpace(componentContains) || ComponentTypeMatches(item, componentContains));
		if ((Object)(object)val2 == (Object)null)
		{
			return BridgeResponse.Failure("Component not found.");
		}
		string text = ExtractObject(request.ArgumentsJson, "argument") ?? ExtractRawValue(request.ArgumentsJson, "argument");
		bool hasArgument = !string.IsNullOrWhiteSpace(text) && !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
		MethodInfo methodInfo = ((object)val2).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, method, StringComparison.OrdinalIgnoreCase) && item.GetParameters().Length == (hasArgument ? 1 : 0));
		if (methodInfo == null)
		{
			return BridgeResponse.Failure(hasArgument ? "Matching one-argument method not found." : "Zero-argument method not found.");
		}
		object[] parameters = null;
		if (hasArgument)
		{
			Type parameterType = methodInfo.GetParameters()[0].ParameterType;
			try
			{
				parameters = new object[1] { ConvertBridgeArgument(text, parameterType) };
			}
			catch (Exception ex)
			{
				return BridgeResponse.Failure("Cannot convert method argument: " + ex.Message);
			}
		}
		methodInfo.Invoke(val2, parameters);
		return BridgeResponse.Success(new
		{
			target = Describe(val, includeComponents: false),
			component = ((object)val2).GetType().FullName,
			method = methodInfo.Name,
			argument = (hasArgument ? text : "")
		});
	}


	private static object ConvertBridgeArgument(string json, Type targetType)
	{
		if (targetType == typeof(string))
		{
			return GetString(json, "value") ?? json.Trim(new char[1] { '"' });
		}
		if (targetType == typeof(bool))
		{
			return bool.Parse(json);
		}
		if (targetType == typeof(int))
		{
			return int.Parse(json, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}
		if (targetType == typeof(long))
		{
			return long.Parse(json, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}
		if (targetType.IsEnum)
		{
			return Enum.Parse(targetType, json.Trim(new char[1] { '"' }), ignoreCase: true);
		}
		return Convert.ChangeType(json.Trim(new char[1] { '"' }), targetType, CultureInfo.InvariantCulture);
	}


	private static string ExtractRawValue(string json, string name)
	{
		Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*(true|false|null|-?\\d+(?:\\.\\d+)?|\"(?:\\\\.|[^\"])*\")", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return null;
		}
		return match.Groups[1].Value;
	}


	private BridgeResponse InvokeNetworkResponse(BridgeRequest request)
	{
		Selector selector = Selector.From(ExtractObject(request.ArgumentsJson, "selector"));
		bool checkValidInput = GetBool(request.ArgumentsJson, "checkValidInput", defaultValue: true);
		bool wantDeckActionOption = GetBool(request.ArgumentsJson, "wantDeckOption", defaultValue: false);
		bool wantDisguisedOption = GetBool(request.ArgumentsJson, "wantDisguisedOption", defaultValue: false);
		GameObject val = ResolveObject(selector);
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("Object not found.");
		}
		Component val2 = FindCardComponent(val);
		if ((Object)(object)val2 == (Object)null)
		{
			return BridgeResponse.Failure("Card component not found.");
		}
		object obj = InvokeNoArg(val2, "GetEntity");
		if (obj == null)
		{
			return BridgeResponse.Failure("Card.GetEntity() returned null.");
		}
		object inputManager = GetInputManager();
		if (inputManager == null)
		{
			return BridgeResponse.Failure("InputManager.Get() returned null.");
		}
		if (TryInvokeBool(obj, "IsBattlegroundTrinket") && TryHandleClickOnChoice(inputManager, obj, out var methodName))
		{
			return BridgeResponse.Success(new
			{
				target = Describe(val, includeComponents: false),
				card = ((object)val2).GetType().FullName,
				entity = SafeEntitySummary(obj),
				accepted = true,
				method = inputManager.GetType().FullName + "." + methodName,
				nativeTrinketChoice = true
			});
		}
		bool? flag = TryDoNetworkResponse(inputManager, obj, checkValidInput, wantDeckActionOption, wantDisguisedOption, out var methodName2);
		if (!flag.HasValue)
		{
			return BridgeResponse.Failure("InputManager.DoNetworkResponse compatible overload was not found.");
		}
		return BridgeResponse.Success(new
		{
			target = Describe(val, includeComponents: false),
			card = ((object)val2).GetType().FullName,
			entity = SafeEntitySummary(obj),
			accepted = flag.Value,
			method = inputManager.GetType().FullName + "." + methodName2
		});
	}


	private BridgeResponse SelectMulliganCard(BridgeRequest request)
	{
		Selector selector = Selector.From(ExtractObject(request.ArgumentsJson, "selector"));
		bool flag = GetBool(request.ArgumentsJson, "replace", defaultValue: true);
		GameObject val = ResolveObject(selector);
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("Mulligan card object not found.");
		}
		Component card = FindCardComponent(val);
		if ((Object)(object)card == (Object)null)
		{
			return BridgeResponse.Failure("Mulligan Card component not found.");
		}
		object obj = InvokeNoArg(card, "GetEntity");
		if (obj == null)
		{
			return BridgeResponse.Failure("Mulligan Card.GetEntity() returned null.");
		}
		object obj2 = InvokeNoArg(obj, "GetEntityId");
		if (obj2 == null)
		{
			return BridgeResponse.Failure("Mulligan entity id is unavailable.");
		}
		int num = Convert.ToInt32(obj2, CultureInfo.InvariantCulture);
		Component val2 = FindMulliganManager();
		if ((Object)(object)val2 == (Object)null)
		{
			return BridgeResponse.Failure("MulliganManager not found.");
		}
		int num2 = ResolveMulliganStartingCardIndex(val2, card, obj, num, flag, out var resolution);
		bool value = false;
		bool flag2 = num2 >= 0 && TryReadBoolArrayElement(val2, "m_handCardsMarkedForReplace", num2, out value);
		if (flag2 && value == flag)
		{
			bool chosenEntityApplied = EnsureChosenEntityState(obj, !flag, out var methodName, out var error);
			return BridgeResponse.Success(new
			{
				target = Describe(val, includeComponents: false),
				component = ((object)val2).GetType().FullName,
				entityId = num,
				index = num2,
				indexResolution = resolution,
				replace = flag,
				alreadySelected = true,
				chosenEntityApplied = chosenEntityApplied,
				chosenEntityMethod = methodName,
				chosenEntityError = error
			});
		}
		string text = "";
		string text2 = null;
		try
		{
			MethodInfo methodInfo = ((object)val2).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo method) =>
			{
				if (!string.Equals(method.Name, "ToggleHoldState", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters2 = method.GetParameters();
				return parameters2.Length == 1 && parameters2[0].ParameterType.IsInstanceOfType(card);
			});
			if (methodInfo == null)
			{
				text2 = "MulliganManager.ToggleHoldState(Card) not found.";
			}
			else
			{
				object[] parameters = (object[])(object)new Component[1] { card };
				methodInfo.Invoke(val2, parameters);
				text = ((object)val2).GetType().FullName + "." + methodInfo.Name + "(Card)";
			}
		}
		catch (Exception ex)
		{
			text2 = ex.ToString();
		}
		bool value2 = false;
		bool num3 = num2 >= 0 && TryReadBoolArrayElement(val2, "m_handCardsMarkedForReplace", num2, out value2) && value2 == flag;
		bool flag3 = false;
		if (!num3 && num2 >= 0)
		{
			flag3 = TrySetBoolArrayElement(val2, "m_handCardsMarkedForReplace", num2, flag) && TryReadBoolArrayElement(val2, "m_handCardsMarkedForReplace", num2, out value2) && value2 == flag;
		}
		bool flag4 = EnsureChosenEntityState(obj, !flag, out var methodName2, out var error2);
		bool flag5 = !string.IsNullOrWhiteSpace(text);
		if (!num3 && !flag3 && !flag4 && !flag5)
		{
			return BridgeResponse.Failure(text2 ?? "Mulligan card replacement state was not applied.");
		}
		return BridgeResponse.Success(new
		{
			target = Describe(val, includeComponents: false),
			component = ((object)val2).GetType().FullName,
			entityId = num,
			index = num2,
			indexResolution = resolution,
			replace = flag,
			beforeMarked = (flag2 ? new bool?(value) : ((bool?)null)),
			afterMarked = value2,
			method = text,
			invokeError = text2,
			usedManualFallback = flag3,
			chosenEntityApplied = flag4,
			chosenEntityMethod = methodName2,
			chosenEntityError = error2
		});
	}


	private static int ResolveMulliganStartingCardIndex(object manager, object selectedCard, object selectedEntity, int selectedEntityId, bool replace, out string resolution)
	{
		List<string> list = new List<string>();
		foreach (MethodInfo item in from method in manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			where string.Equals(method.Name, "GetStartingCardIndexOfEntity", StringComparison.Ordinal) && CanPassInt(method.ReturnType) && method.GetParameters().Length == 1
			select method)
		{
			Type parameterType = item.GetParameters()[0].ParameterType;
			object obj;
			string text;
			if (selectedEntity != null && parameterType.IsInstanceOfType(selectedEntity))
			{
				obj = selectedEntity;
				text = "Entity";
			}
			else
			{
				if (!CanPassInt(parameterType))
				{
					list.Add(parameterType.Name + "=unsupported");
					continue;
				}
				obj = ConvertIntArgument(selectedEntityId, parameterType);
				text = "entityId";
			}
			try
			{
				object obj2 = item.Invoke(manager, new object[1] { obj });
				int num = ((obj2 == null) ? (-1) : Convert.ToInt32(obj2, CultureInfo.InvariantCulture));
				list.Add($"{parameterType.Name}/{text}={num}");
				if (num >= 0)
				{
					resolution = "GetStartingCardIndexOfEntity(" + text + ")";
					return num;
				}
			}
			catch (Exception ex)
			{
				list.Add(parameterType.Name + "/" + text + " threw " + ex.GetType().Name);
			}
		}
		int num2 = FindMulliganStartingCardIndex(manager, selectedCard, selectedEntity, selectedEntityId, replace, out var resolution2);
		if (num2 < 0)
		{
			int num3 = ReadIntMember(selectedEntity, "GetZonePosition", 0);
			if (num3 > 0)
			{
				num2 = num3 - 1;
				resolution2 = $"entity zone position {num3}";
			}
		}
		resolution = ((list.Count == 0) ? resolution2 : (resolution2 + "; direct: " + string.Join(", ", list)));
		return num2;
	}


	private static int FindMulliganStartingCardIndex(object manager, object selectedCard, object selectedEntity, int selectedEntityId, bool replace, out string resolution)
	{
		resolution = "starting cards unavailable";
		List<object> list = MaterializeMulliganCards(InvokeNoArg(manager, "GetStartingCards"));
		string text = "GetStartingCards()";
		if (list == null)
		{
			list = MaterializeMulliganCards((manager?.GetType().GetField("m_startingCards", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(manager));
			text = "m_startingCards";
		}
		if (list == null)
		{
			return -1;
		}
		List<string> list2 = new List<string>();
		resolution = $"{text} count={list.Count}, no match";
		string text2 = InvokeNoArg(selectedEntity, "GetCardId")?.ToString() ?? "";
		int num = -1;
		for (int i = 0; i < list.Count; i++)
		{
			object obj = list[i];
			if (obj == null)
			{
				continue;
			}
			if (obj == selectedCard)
			{
				resolution = text + " card reference";
				return i;
			}
			object obj2 = InvokeNoArg(obj, "GetEntity");
			if (obj2 == null && InvokeNoArg(obj, "GetEntityId") != null)
			{
				obj2 = obj;
			}
			if (obj2 == null)
			{
				list2.Add($"{i}:{obj.GetType().Name}/no-entity");
				continue;
			}
			if (obj2 == selectedEntity)
			{
				resolution = text + " entity reference";
				return i;
			}
			object obj3 = InvokeNoArg(obj2, "GetEntityId");
			string text3 = InvokeNoArg(obj2, "GetCardId")?.ToString() ?? "";
			list2.Add(string.Format("{0}:{1}/entity={2}/card={3}", i, obj.GetType().Name, obj3 ?? "?", text3));
			if (obj3 != null && Convert.ToInt32(obj3, CultureInfo.InvariantCulture) == selectedEntityId)
			{
				resolution = text + " entity id";
				return i;
			}
			if (!string.IsNullOrWhiteSpace(text2) && string.Equals(text3, text2, StringComparison.OrdinalIgnoreCase))
			{
				num = ((num < 0) ? i : num);
				if (TryReadBoolArrayElement(manager, "m_handCardsMarkedForReplace", i, out var value) && value != replace)
				{
					resolution = text + " card id and pending state";
					return i;
				}
			}
		}
		if (num >= 0)
		{
			resolution = text + " card id";
		}
		else if (list2.Count > 0)
		{
			resolution = $"{text} count={list.Count}, selected={selectedEntityId}/{text2}, " + "candidates=[" + string.Join(", ", list2.Take(8)) + "]";
		}
		return num;
	}


	private static List<object> MaterializeMulliganCards(object value)
	{
		if (!(value is IEnumerable enumerable) || value is string)
		{
			return null;
		}
		List<object> list = new List<object>();
		foreach (object item in enumerable)
		{
			list.Add(item);
		}
		return list;
	}


	private BridgeResponse SelectMulliganHero(BridgeRequest request)
	{
		GameObject val = ResolveObject(Selector.From(ExtractObject(request.ArgumentsJson, "selector")));
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("Hero card object not found.");
		}
		Component val2 = FindCardComponent(val);
		if ((Object)(object)val2 == (Object)null)
		{
			return BridgeResponse.Failure("Hero Card component not found.");
		}
		object obj = InvokeNoArg(val2, "GetEntity");
		if (obj == null)
		{
			return BridgeResponse.Failure("Hero Card.GetEntity() returned null.");
		}
		object obj2 = InvokeNoArg(obj, "GetEntityId");
		if (obj2 == null)
		{
			return BridgeResponse.Failure("Hero entity id is unavailable.");
		}
		int num = Convert.ToInt32(obj2, CultureInfo.InvariantCulture);
		Component val3 = FindMulliganManager();
		if ((Object)(object)val3 == (Object)null)
		{
			return BridgeResponse.Failure("MulliganManager not found.");
		}
		object inputManager = GetInputManager();
		bool? flag = ((inputManager == null) ? ((bool?)null) : TryDoNetworkResponse(inputManager, obj, checkValidInput: true, wantDeckActionOption: false, wantDisguisedOption: false, out var methodName));
		if (((!flag) ?? true) && inputManager != null)
		{
			flag = TryDoNetworkResponse(inputManager, obj, checkValidInput: false, wantDeckActionOption: false, wantDisguisedOption: false, out methodName);
		}
		Type type = FindLoadedType("Network+MulliganChooseOneTentativeSelection") ?? FindLoadedType("Network.MulliganChooseOneTentativeSelection") ?? FindLoadedType("MulliganChooseOneTentativeSelection");
		if (type == null)
		{
			return BridgeResponse.Failure("Network.MulliganChooseOneTentativeSelection type not found.");
		}
		object selection = Activator.CreateInstance(type);
		type.GetProperty("EntityId")?.SetValue(selection, num, null);
		type.GetProperty("IsConfirmation")?.SetValue(selection, false, null);
		type.GetProperty("IsFromTeammate")?.SetValue(selection, false, null);
		MethodInfo methodInfo = ((object)val3).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, "OnMulliganChooseOneTentativeSelection", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(selection);
		});
		if (methodInfo == null)
		{
			return BridgeResponse.Failure("MulliganManager.OnMulliganChooseOneTentativeSelection(message) not found.");
		}
		methodInfo.Invoke(val3, new object[1] { selection });
		return BridgeResponse.Success(new
		{
			target = Describe(val, includeComponents: false),
			component = ((object)val3).GetType().FullName,
			method = methodInfo.Name,
			entityId = num,
			networkAccepted = (flag ?? false)
		});
	}


	private BridgeResponse ConfirmMulliganHero(BridgeRequest request)
	{
		Component val = FindMulliganManager();
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("MulliganManager not found.");
		}
		int selectedHeroEntityId = GetInt(request.ArgumentsJson, "entityId", 0);
		List<int> intArray = GetIntArray(request.ArgumentsJson, "keptEntityIds");
		bool flag = TrySubmitMulliganChoices(val, selectedHeroEntityId, intArray, out var details, out var error);
		if (flag)
		{
			bool localCleanupCompleted = TryInvokeSingleBoolArg(val, "AutomaticContinueMulligan", value: false);
			return BridgeResponse.Success(new
			{
				component = ((object)val).GetType().FullName,
				method = "Network.SendChoices+MulliganManager.AutomaticContinueMulligan",
				selectedHeroEntityId = selectedHeroEntityId,
				choicesSubmitted = flag,
				localCleanupCompleted = localCleanupCompleted,
				choiceDetails = details,
				choiceError = error
			});
		}
		MethodInfo methodInfo = ((object)val).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, "OnMulliganButtonReleased", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && string.Equals(parameters[0].ParameterType.Name, "UIEvent", StringComparison.Ordinal);
		});
		if (methodInfo == null)
		{
			return BridgeResponse.Failure("MulliganManager.OnMulliganButtonReleased(UIEvent) not found.");
		}
		methodInfo.Invoke(val, new object[1]);
		return BridgeResponse.Success(new
		{
			component = ((object)val).GetType().FullName,
			method = methodInfo.Name,
			selectedHeroEntityId = selectedHeroEntityId,
			choicesSubmitted = flag,
			choiceDetails = details,
			choiceError = error
		});
	}


	private BridgeResponse ConfirmFriendlyChoice(bool requireMagicItemDiscover)
	{
		Component val = FindSingletonComponent("ChoiceCardMgr");
		if ((Object)(object)val == (Object)null)
		{
			return BridgeResponse.Failure("ChoiceCardMgr not found.");
		}
		if (requireMagicItemDiscover && !TryInvokeBool(val, "IsFriendlyMagicItemDiscover"))
		{
			return BridgeResponse.Failure("The current choice is not a trinket choice.");
		}
		GameObject[] array = ReadChoiceCardObjects(val).ToArray();
		if (array.Length == 0)
		{
			return BridgeResponse.Failure("No current friendly choice cards were found.");
		}
		MethodInfo methodInfo = ((object)val).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, "ConfirmChoiceButton_OnRelease", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && string.Equals(parameters[0].ParameterType.Name, "UIEvent", StringComparison.Ordinal);
		});
		if (methodInfo == null)
		{
			return BridgeResponse.Failure("ChoiceCardMgr.ConfirmChoiceButton_OnRelease(UIEvent) not found.");
		}
		methodInfo.Invoke(val, new object[1]);
		return BridgeResponse.Success(new
		{
			component = ((object)val).GetType().FullName,
			method = methodInfo.Name,
			choiceCardCount = array.Length
		});
	}


	private static string ResolveOptionMainCardId(object gameState, object main, int mainEntityId)
	{
		return ResolveOptionMainCardId(main, ResolveOptionMainEntity(gameState, main, mainEntityId));
	}


	private static object ResolveOptionMainEntity(object gameState, object main, int mainEntityId)
	{
		object obj = InvokeFirstNoArg(main, "get_Entity", "GetEntity");
		if (obj == null && mainEntityId > 0)
		{
			obj = InvokeIntArgObject(gameState, "GetEntity", mainEntityId) ?? InvokeIntArgObject(gameState, "GetEntityByID", mainEntityId);
		}
		return obj;
	}


	private static string ResolveOptionMainCardId(object main, object entity)
	{
		string text = InvokeFirstNoArg(main, "get_CardId", "GetCardId")?.ToString();
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return InvokeFirstNoArg(entity, "GetCardId", "get_CardId")?.ToString() ?? "";
	}


	private static bool IsHandEntity(object entity)
	{
		return string.Equals(InvokeNoArg(entity, "GetZone")?.ToString(), "HAND", StringComparison.OrdinalIgnoreCase);
	}


	private static bool IsDirectConstructedAttackOption(object gameState, object option, object main, object sourceEntity, int targetEntityId, int subOptionIndex, int boardPosition)
	{
		if (gameState == null || option == null || main == null || sourceEntity == null || targetEntityId <= 0 || subOptionIndex >= 0 || boardPosition != 0 || ReadIntMember(option, "get_Type", int.MinValue) != 3 || ReadIntMember(main, "get_PowerKeyword", int.MinValue) != 0 || !string.Equals(InvokeNoArg(sourceEntity, "GetZone")?.ToString(), "PLAY", StringComparison.OrdinalIgnoreCase) || (!TryInvokeBool(sourceEntity, "IsMinion") && !TryInvokeBool(sourceEntity, "IsHero")))
		{
			return false;
		}
		if (InvokeNoArg(option, "get_Subs") is IEnumerable enumerable)
		{
			{
				IEnumerator enumerator = enumerable.GetEnumerator();
				try
				{
					if (enumerator.MoveNext())
					{
						_ = enumerator.Current;
						return false;
					}
				}
				finally
				{
					IDisposable disposable = enumerator as IDisposable;
					if (disposable != null)
					{
						disposable.Dispose();
					}
				}
			}
		}
		object instance = InvokeIntArgObject(gameState, "GetEntity", targetEntityId) ?? InvokeIntArgObject(gameState, "GetEntityByID", targetEntityId);
		int num = ReadIntMember(sourceEntity, "GetControllerId", 0);
		int num2 = ReadIntMember(instance, "GetControllerId", 0);
		if (num > 0 && num2 > 0)
		{
			return num != num2;
		}
		return true;
	}


	private static int ReadUserUiEntityId(object entity)
	{
		return ReadIntMember(entity, "GetEntityId", ReadIntMember(entity, "get_ID", 0));
	}


	private static void SendOptionWithProcessUserUi(object gameState, int sourceEntityId, int targetEntityId, bool holdSource, string action, bool trackAttackCandidate = false)
	{

		try
		{
			long token = 0;
			try
			{
				InvokeNoArg(gameState, "SendOption");
			}
			catch
			{

				throw;
			}
		}
		finally
		{

		}
	}


	private BridgeResponse SubmitOptionTarget(int heldEntityId, int? requiredMainEntityId, int? optionType, string failurePrefix, GameObject targetObject, bool trackAttackCandidate = false)
	{
		if (heldEntityId <= 0)
		{
			return BridgeResponse.Failure("Target entityId is required.");
		}
		object gameState = GetGameState();
		if (gameState == null)
		{
			return BridgeResponse.Failure("GameState.Get() returned null.");
		}
		if (!(InvokeNoArg(InvokeNoArg(gameState, "GetOptionsPacket"), "get_List") is IEnumerable enumerable))
		{
			return BridgeResponse.Failure("GameState.GetOptionsPacket().List was not available.");
		}
		List<object> list = new List<object>();
		int num = -1;
		int num2 = -1;
		object option = null;
		object main = null;
		int num3 = 0;
		foreach (object item in enumerable)
		{
			int num4 = ReadIntMember(item, "get_Type", int.MinValue);
			if (optionType.HasValue && num4 != optionType.Value)
			{
				num3++;
				continue;
			}
			object obj = InvokeNoArg(item, "get_Main");
			int num5 = ReadIntMember(obj, "get_ID", -1);
			bool flag = !requiredMainEntityId.HasValue || num5 == requiredMainEntityId.Value;
			bool flag2 = flag && TryInvokeIntArgBool(obj, "IsValidTarget", heldEntityId);
			object playError = DescribePlayErrorInfo(InvokeNoArg(obj, "get_PlayErrorInfo"));
			list.Add(new
			{
				index = num3,
				type = num4,
				mainEntityId = num5,
				requiredMainEntityId = requiredMainEntityId,
				mainMatches = flag,
				validTarget = flag2,
				playError = playError
			});
			if (flag2 && num < 0)
			{
				num = num3;
				num2 = num5;
				option = item;
				main = obj;
			}
			num3++;
		}
		if (num < 0)
		{
			return BridgeResponse.Failure($"{failurePrefix} {heldEntityId}.", new
			{
				targetEntityId = heldEntityId,
				requiredMainEntityId = requiredMainEntityId,
				optionType = optionType,
				target = (((Object)(object)targetObject == (Object)null) ? null : Describe(targetObject, includeComponents: false)),
				candidates = list.Take(16).ToArray()
			});
		}
		if (!InvokeIntArg(gameState, "SetSelectedOption", num))
		{
			return BridgeResponse.Failure("GameState.SetSelectedOption(int) not found.");
		}
		if (!InvokeIntArg(gameState, "SetSelectedOptionTarget", heldEntityId))
		{
			return BridgeResponse.Failure("GameState.SetSelectedOptionTarget(int) not found.");
		}
		object obj2 = InvokeIntArgObject(gameState, "GetEntity", num2) ?? InvokeIntArgObject(gameState, "GetEntityByID", num2);
		bool trackAttackCandidate2 = trackAttackCandidate && IsDirectConstructedAttackOption(gameState, option, main, obj2, heldEntityId, -1, 0);
		SendOptionWithProcessUserUi(gameState, num2, heldEntityId, IsHandEntity(obj2), "optionTarget", trackAttackCandidate2);
		return BridgeResponse.Success(new
		{
			accepted = true,
			submitted = true,
			targetEntityId = heldEntityId,
			requiredMainEntityId = requiredMainEntityId,
			optionType = optionType,
			target = (((Object)(object)targetObject == (Object)null) ? null : Describe(targetObject, includeComponents: false)),
			optionIndex = num,
			mainEntityId = num2,
			candidates = list.Take(16).ToArray()
		});
	}


	private BridgeResponse DismissStartScreen(BridgeRequest request)
	{
		bool flag = GetBool(request.ArgumentsJson, "dryRun", defaultValue: false);
		bool flag2 = GetBool(request.ArgumentsJson, "force", defaultValue: false);
		MainMenuReadiness mainMenuReadiness = ReadMainMenuReadiness();
		if (mainMenuReadiness.activeGameplay || mainMenuReadiness.startupPending || mainMenuReadiness.atMainMenu)
		{
			return BridgeResponse.Success(new
			{
				visible = false,
				dismissed = false,
				dismissable = false,
				dryRun = flag,
				boxState = mainMenuReadiness.boxState,
				startupPending = mainMenuReadiness.startupPending,
				startupPopupKind = mainMenuReadiness.startupPopupKind
			});
		}
		GameObject val = FindChinaStartClickTarget();
		if ((Object)(object)val != (Object)null)
		{
			string text = (flag ? null : (TryInvokePegUiPressRelease(val) ?? TryInvokePegUiClick(val)));
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = (text != null),
				dismissable = true,
				dryRun = flag,
				boxState = mainMenuReadiness.boxState,
				method = (text ?? ""),
				target = Describe(val, includeComponents: false)
			});
		}
		string text2 = ReadBoxState(FindTheBoxObject());
		bool flag3 = IsStartScreenBoxState(text2);
		GameObject[] array = (from gameObject in EnumerateObjects()
			where ((Object)gameObject).name.IndexOf("StartText", StringComparison.OrdinalIgnoreCase) >= 0 || ((Object)gameObject).name.IndexOf("China_Ratings_SplashScreen", StringComparison.OrdinalIgnoreCase) >= 0 || ((Object)gameObject).name.IndexOf("Startup_Hub", StringComparison.OrdinalIgnoreCase) >= 0 || ((Object)gameObject).name.IndexOf("Startup_Tutorial", StringComparison.OrdinalIgnoreCase) >= 0 || ((Object)gameObject).name.IndexOf("Startup_SetRotation", StringComparison.OrdinalIgnoreCase) >= 0 || GetObjectText(gameObject).IndexOf("点击开始", StringComparison.OrdinalIgnoreCase) >= 0
			select gameObject).Take(16).ToArray();
		GameObject val2 = FindPostStartupVisibleObject();
		bool num = (Object)(object)val2 == (Object)null && (flag3 || (string.IsNullOrWhiteSpace(text2) && array.Length != 0));
		bool flag4 = IsStartScreenDismissable(text2, array);
		if (!num)
		{
			return BridgeResponse.Success(new
			{
				visible = false,
				dismissed = false,
				dryRun = flag,
				dismissable = false,
				boxState = text2,
				startupObjectCount = array.Length,
				postStartupObject = (((Object)(object)val2 == (Object)null) ? null : Describe(val2, includeComponents: false)),
				method = ""
			});
		}
		if (flag)
		{
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				dryRun = true,
				dismissable = flag4,
				boxState = text2,
				startupObjectCount = array.Length,
				postStartupObject = (((Object)(object)val2 == (Object)null) ? null : Describe(val2, includeComponents: false)),
				targets = array.Select((GameObject item) => Describe(item, includeComponents: false)).ToArray()
			});
		}
		if (!flag4 && !flag2)
		{
			return BridgeResponse.Success(new
			{
				visible = true,
				dismissed = false,
				dryRun = flag,
				dismissable = false,
				boxState = text2,
				startupObjectCount = array.Length,
				postStartupObject = (((Object)(object)val2 == (Object)null) ? null : Describe(val2, includeComponents: false)),
				reason = "Start screen is visible but the clickable China ratings/start target has not appeared yet.",
				targets = array.Select((GameObject item) => Describe(item, includeComponents: false)).ToArray()
			});
		}
		List<string> list = new List<string>();
		int num2 = 0;
		foreach (GameObject item in ExpandStartScreenClickTargets(array))
		{
			string text3 = TryInvokeBoxStartButton(item) ?? TryInvokePegUiClick(item) ?? TryInvokeCommonClickMethod(item) ?? TryInvokeLikelyStartMethod(item) ?? TrySendPlayMakerStartScreenEvents(item);
			if (text3 != null)
			{
				list.Add(GetPath(item) + "::" + text3);
			}
			SendCommonClickMessages(item);
			num2++;
		}
		return BridgeResponse.Success(new
		{
			visible = true,
			dismissed = (list.Count > 0),
			dryRun = flag,
			dismissable = flag4,
			boxState = text2,
			methods = list.Take(16).ToArray(),
			messageTargets = num2,
			targets = array.Select((GameObject item) => Describe(item, includeComponents: false)).ToArray()
		});
	}


	private static void SendCommonClickMessages(GameObject gameObject)
	{
		string[] array = new string[7] { "OnMouseEnter", "OnPress", "OnMouseDown", "OnClick", "OnMouseUp", "OnRelease", "OnMouseExit" };
		foreach (string text in array)
		{
			gameObject.SendMessage(text, (SendMessageOptions)1);
		}
	}


	private static string TryInvokeCommonClickMethod(GameObject gameObject)
	{
		string[] array = new string[10] { "OnClick", "Click", "OnButtonClick", "OnButtonClicked", "OnPressed", "OnPress", "OnRelease", "OnMouseUpAsButton", "HandleClick", "HandleRelease" };
		foreach (Component item in from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component)
		{
			string[] array2 = array;
			foreach (string name in array2)
			{
				MethodInfo methodInfo = ((object)item).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase) && item.GetParameters().Length == 0);
				if (!(methodInfo == null))
				{
					methodInfo.Invoke(item, null);
					return ((object)item).GetType().FullName + "." + methodInfo.Name + "()";
				}
			}
		}
		return null;
	}


	private static string TryInvokeLikelyStartMethod(GameObject gameObject)
	{
		string[] source = new string[11]
		{
			"click", "press", "release", "start", "continue", "confirm", "accept", "dismiss", "close", "done",
			"complete"
		};
		foreach (Component item in from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component)
		{
			string fullName = ((object)item).GetType().FullName;
			if (fullName.IndexOf("Splash", StringComparison.OrdinalIgnoreCase) < 0 && fullName.IndexOf("Start", StringComparison.OrdinalIgnoreCase) < 0 && fullName.IndexOf("Rating", StringComparison.OrdinalIgnoreCase) < 0 && fullName.IndexOf("Age", StringComparison.OrdinalIgnoreCase) < 0 && fullName.IndexOf("China", StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			MethodInfo[] methods = ((object)item).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (MethodInfo method in methods)
			{
				if (method.GetParameters().Length == 0 && !method.IsSpecialName && source.Any((string hint) => method.Name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
				{
					method.Invoke(item, null);
					return ((object)item).GetType().FullName + "." + method.Name + "()";
				}
			}
		}
		return null;
	}


	private static string TryInvokePegUiClick(GameObject gameObject)
	{
		foreach (Component item in from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component)
		{
			Type type = ((object)item).GetType();
			if (type.FullName.IndexOf("PegUIElement", StringComparison.OrdinalIgnoreCase) < 0 && !HasNoArgMethod(type, "TriggerPress") && !HasNoArgMethod(type, "TriggerRelease") && !HasNoArgMethod(type, "TriggerTap"))
			{
				continue;
			}
			List<string> list = new List<string>();
			string[] array = new string[5] { "TriggerOver", "TriggerPress", "TriggerRelease", "TriggerTap", "TriggerOut" };
			foreach (string name in array)
			{
				MethodInfo methodInfo = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, name, StringComparison.Ordinal) && item.GetParameters().Length == 0);
				if (!(methodInfo == null))
				{
					methodInfo.Invoke(item, null);
					list.Add(methodInfo.Name);
				}
			}
			if (list.Count > 0)
			{
				return type.FullName + "." + string.Join("+", list.ToArray()) + "()";
			}
		}
		return null;
	}


	private static string TryInvokePegUiPressRelease(GameObject gameObject)
	{
		foreach (Component item in from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component)
		{
			Type type = ((object)item).GetType();
			if (type.FullName.IndexOf("PegUIElement", StringComparison.OrdinalIgnoreCase) < 0 && !HasNoArgMethod(type, "TriggerPress") && !HasNoArgMethod(type, "TriggerRelease"))
			{
				continue;
			}
			List<string> list = new List<string>();
			string[] array = new string[2] { "TriggerPress", "TriggerRelease" };
			foreach (string name in array)
			{
				MethodInfo methodInfo = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, name, StringComparison.Ordinal) && item.GetParameters().Length == 0);
				if (!(methodInfo == null))
				{
					methodInfo.Invoke(item, null);
					list.Add(methodInfo.Name);
				}
			}
			if (list.Count == 2)
			{
				return type.FullName + "." + string.Join("+", list.ToArray()) + "()";
			}
		}
		return null;
	}


	private static bool TryInvokeZeroArgMethod(object instance, string name, out string methodName, out string error)
	{
		object result;
		return TryInvokeZeroArgMethodWithResult(instance, name, out result, out methodName, out error);
	}


	private static bool TryInvokeZeroArgMethodWithResult(object instance, string name, out object result, out string methodName, out string error)
	{
		result = null;
		methodName = "";
		error = "";
		if (instance == null)
		{
			error = "Target instance is null.";
			return false;
		}
		MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, name, StringComparison.Ordinal) && item.GetParameters().Length == 0);
		if (methodInfo == null)
		{
			error = "Zero-argument method " + name + " not found.";
			return false;
		}
		try
		{
			result = methodInfo.Invoke(instance, null);
			methodName = instance.GetType().FullName + "." + methodInfo.Name + "()";
			return true;
		}
		catch (TargetInvocationException ex)
		{
			error = ex.InnerException?.Message ?? ex.Message;
			return false;
		}
		catch (Exception ex2)
		{
			error = ex2.Message;
			return false;
		}
	}


	private static bool TryInvokeSingleArgMethod(object instance, string name, object argument, out string methodName, out string error)
	{
		methodName = "";
		error = "";
		if (instance == null || argument == null)
		{
			error = "Target instance or argument is null.";
			return false;
		}
		Type argumentType = argument.GetType();
		MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, name, StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(argumentType);
		});
		if (methodInfo == null)
		{
			error = "Single-argument method " + name + " not found for " + argumentType.FullName + ".";
			return false;
		}
		try
		{
			methodInfo.Invoke(instance, new object[1] { argument });
			methodName = instance.GetType().FullName + "." + methodInfo.Name + "(" + argumentType.FullName + ")";
			return true;
		}
		catch (TargetInvocationException ex)
		{
			error = ex.InnerException?.Message ?? ex.Message;
			return false;
		}
		catch (Exception ex2)
		{
			error = ex2.Message;
			return false;
		}
	}


	private static bool? TryInvokeSingleArgNullableBool(object instance, string name, object argument)
	{
		if (instance == null || argument == null)
		{
			return null;
		}
		try
		{
			Type argumentType = argument.GetType();
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, name, StringComparison.Ordinal) || item.ReturnType != typeof(bool))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(argumentType);
			});
			if (methodInfo == null)
			{
				return null;
			}
			return (methodInfo.Invoke(instance, new object[1] { argument }) is bool value) ? new bool?(value) : ((bool?)null);
		}
		catch
		{
			return null;
		}
	}


	private static string FormatNullableBool(bool? value)
	{
		if (value.HasValue)
		{
			if (value == true)
			{
				return "true";
			}
			return "false";
		}
		return "unknown";
	}


	private static bool HasNoArgMethod(Type type, string name)
	{
		return type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Any((MethodInfo method) => string.Equals(method.Name, name, StringComparison.Ordinal) && method.GetParameters().Length == 0);
	}


	private static string TryInvokeBoxStartButton(GameObject gameObject)
	{
		foreach (Component item in from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component)
		{
			Type type = ((object)item).GetType();
			if (!string.Equals(type.FullName, "Box", StringComparison.Ordinal) && !string.Equals(type.Name, "Box", StringComparison.Ordinal))
			{
				continue;
			}
			MethodInfo methodInfo = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo method) => string.Equals(method.Name, "OnStartButtonPressed", StringComparison.Ordinal) && method.GetParameters().Length == 1);
			if (methodInfo != null)
			{
				methodInfo.Invoke(item, new object[1]);
				return type.FullName + "." + methodInfo.Name + "(null)";
			}
			MethodInfo methodInfo2 = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo method) =>
			{
				if (!string.Equals(method.Name, "FireButtonPressEvent", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters2 = method.GetParameters();
				return parameters2.Length == 2 && parameters2[0].ParameterType.IsEnum && parameters2[1].ParameterType == typeof(bool);
			});
			if (methodInfo2 != null)
			{
				ParameterInfo[] parameters = methodInfo2.GetParameters();
				methodInfo2.Invoke(item, new object[2]
				{
					Enum.ToObject(parameters[0].ParameterType, 0),
					false
				});
				return type.FullName + "." + methodInfo2.Name + "(ButtonType.0,false)";
			}
		}
		return null;
	}


	private static GameObject FindTheBoxObject()
	{
		GameObject val = null;
		foreach (GameObject item in EnumerateObjects(sort: false))
		{
			if (string.Equals(GetPath(item), "/TheBox(Clone)", StringComparison.OrdinalIgnoreCase))
			{
				return item;
			}
			if ((Object)(object)val == (Object)null && string.Equals(((Object)item).name, "TheBox(Clone)", StringComparison.OrdinalIgnoreCase))
			{
				val = item;
			}
		}
		return val;
	}


	private static string ReadBoxState(GameObject box)
	{
		return ReadComponentState((box != null) ? (from item in box.GetComponents<Component>()
			where (Object)(object)item != (Object)null
			select item).FirstOrDefault((Component item) => string.Equals(((object)item).GetType().Name, "Box", StringComparison.Ordinal)) : null);
	}


	private static string ReadComponentState(Component component)
	{
		if ((Object)(object)component == (Object)null)
		{
			return "";
		}
		try
		{
			return InvokeNoArg(component, "GetState")?.ToString() ?? ReadMemberAsString(component, "m_state") ?? "";
		}
		catch
		{
			return ReadMemberAsString(component, "m_state") ?? "";
		}
	}


	private static bool IsStartScreenBoxState(string boxState)
	{
		return ContainsAny(boxState, "STARTUP", "PRESS_START");
	}


	private static bool IsStartScreenDismissable(string boxState, IReadOnlyCollection<GameObject> startObjects)
	{
		if (ContainsAny(boxState, "PRESS_START"))
		{
			return true;
		}
		foreach (GameObject startObject in startObjects)
		{
			if (ContainsAny(((Object)startObject).name ?? "", "China_Ratings_SplashScreen", "StartText") || GetObjectText(startObject).IndexOf("点击开始", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}


	private static GameObject FindPostStartupVisibleObject()
	{
		foreach (GameObject item in EnumerateObjects())
		{
			if (item.activeInHierarchy)
			{
				string text = ((Object)item).name ?? "";
				string path = GetPath(item);
				if (IsLikelyQuestNotificationPopupObject(text, path) || text.IndexOf("QuestLogPanel", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("QuestLogPanel", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("BaconLobby", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/Gameplay/", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return item;
				}
			}
		}
		return null;
	}


	private static string TrySendPlayMakerStartScreenEvents(GameObject gameObject)
	{
		string[] array = new string[17]
		{
			"CLICK", "Click", "Clicked", "MOUSE DOWN", "MOUSE UP", "MouseDown", "MouseUp", "RELEASE", "Release", "START",
			"Start", "BEGIN", "Begin", "CONTINUE", "Continue", "FINISHED", "Finished"
		};
		foreach (Component item in from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component)
		{
			Type type = ((object)item).GetType();
			if (type.FullName.IndexOf("PlayMakerFSM", StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			MethodInfo methodInfo = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "SendEvent", StringComparison.Ordinal) && item.GetParameters().Length == 1 && item.GetParameters()[0].ParameterType == typeof(string));
			if (methodInfo == null)
			{
				continue;
			}
			List<string> list = new List<string>();
			string[] array2 = array;
			foreach (string text in array2)
			{
				try
				{
					methodInfo.Invoke(item, new object[1] { text });
					list.Add(text);
				}
				catch
				{
				}
			}
			if (list.Count > 0)
			{
				return type.FullName + "." + methodInfo.Name + "(" + string.Join("|", list.Take(6).ToArray()) + ")";
			}
		}
		return null;
	}


	private static IEnumerable<GameObject> ExpandStartScreenClickTargets(IEnumerable<GameObject> startObjects)
	{
		HashSet<int> seen = new HashSet<int>();
		foreach (GameObject startObject in startObjects)
		{
			Transform current2 = startObject.transform;
			int depth = 0;
			while ((Object)(object)current2 != (Object)null && depth++ < 8)
			{
				if (seen.Add(((Object)((Component)current2).gameObject).GetInstanceID()))
				{
					yield return ((Component)current2).gameObject;
				}
				if (((Object)current2).name.IndexOf("China_Ratings_SplashScreen", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					foreach (Transform item in current2)
					{
						Transform val = item;
						if ((Object)(object)val != (Object)null && seen.Add(((Object)((Component)val).gameObject).GetInstanceID()))
						{
							yield return ((Component)val).gameObject;
						}
					}
				}
				if (((Object)current2).name.IndexOf("EventTable", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					foreach (Transform item2 in current2)
					{
						Transform val2 = item2;
						if (!((Object)(object)val2 == (Object)null))
						{
							string name = ((Object)val2).name;
							if ((name.IndexOf("Startup_", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0) && seen.Add(((Object)((Component)val2).gameObject).GetInstanceID()))
							{
								yield return ((Component)val2).gameObject;
							}
						}
					}
				}
				current2 = current2.parent;
			}
		}
	}


	private static Component FindCardComponent(GameObject gameObject)
	{
		Type type = FindLoadedComponentType("Card");
		if (type != null)
		{
			return gameObject.GetComponent(type) ?? gameObject.GetComponentInChildren(type, true);
		}
		return (from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, "Card", StringComparison.Ordinal)) ?? (from component in gameObject.GetComponentsInChildren<Component>(true)
			where (Object)(object)component != (Object)null
			select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, "Card", StringComparison.Ordinal));
	}


	private static IEnumerable<GameObject> EnumerateCardObjects(object value)
	{
		if (value == null)
		{
			yield break;
		}
		GameObject val = (GameObject)((value is GameObject) ? value : null);
		if (val != null)
		{
			if (TryGetInstanceId(val, out var _))
			{
				yield return val;
			}
			yield break;
		}
		Component val2 = (Component)((value is Component) ? value : null);
		if (val2 != null)
		{
			if (TryGetGameObject(val2, out var gameObject))
			{
				yield return gameObject;
			}
			yield break;
		}
		if (value is Array array)
		{
			foreach (object item in array)
			{
				foreach (GameObject item2 in EnumerateCardObjects(item))
				{
					yield return item2;
				}
			}
			yield break;
		}
		int count;
		if (value is IList list)
		{
			count = Math.Min(list.Count, 12);
			for (int index = 0; index < count; index++)
			{
				object value2;
				try
				{
					value2 = list[index];
				}
				catch
				{
					continue;
				}
				foreach (GameObject item3 in EnumerateCardObjects(value2))
				{
					yield return item3;
				}
			}
			yield break;
		}
		Type type = value.GetType();
		PropertyInfo property = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo indexer = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((PropertyInfo propertyInfo) => propertyInfo.GetIndexParameters().Length == 1);
		if (!(property != null) || !(indexer != null))
		{
			yield break;
		}
		object value3 = property.GetValue(value, null);
		count = ((value3 != null) ? Math.Min(Convert.ToInt32(value3, CultureInfo.InvariantCulture), 12) : 0);
		for (int index = 0; index < count; index++)
		{
			object value4;
			try
			{
				value4 = indexer.GetValue(value, new object[1] { index });
			}
			catch
			{
				continue;
			}
			foreach (GameObject item4 in EnumerateCardObjects(value4))
			{
				yield return item4;
			}
		}
	}


	private static IEnumerable<GameObject> ReadChoiceCardObjects(Component manager)
	{
		HashSet<int> seen = new HashSet<int>();
		string[] array = new string[1] { "GetFriendlyCards" };
		foreach (string methodName in array)
		{
			object value;
			try
			{
				value = InvokeNoArg(manager, methodName);
			}
			catch
			{
				continue;
			}
			foreach (GameObject item in EnumerateCardObjects(value))
			{
				if (TryGetInstanceId(item, out var instanceId) && seen.Add(instanceId))
				{
					yield return item;
				}
			}
		}
	}


	private static object TryDescribeLiteChoiceCard(GameObject gameObject)
	{
		try
		{
			Component val = FindCardComponent(gameObject);
			return ((Object)(object)val == (Object)null) ? null : DescribeLiteCard(gameObject, val);
		}
		catch
		{
			return null;
		}
	}


	private static object TryDescribeLiteLogicalChoiceCard(object gameState, int entityId, int choiceIndex)
	{
		try
		{
			object obj = InvokeIntArgObject(gameState, "GetEntity", entityId) ?? InvokeIntArgObject(gameState, "GetEntityByID", entityId);
			if (obj == null)
			{
				return null;
			}
			string text = InvokeNoArg(obj, "GetCardId")?.ToString() ?? "";
			string arg = InvokeNoArg(obj, "GetName")?.ToString() ?? "";
			string arg2 = InvokeNoArg(obj, "GetZone")?.ToString() ?? "SETASIDE";
			int num = ReadIntMember(obj, "GetControllerId", 0);
			string arg3 = (string.IsNullOrWhiteSpace(text) ? "_" : text);
			string name = $"{arg} [id={entityId} cardId={arg3} " + $"zone={arg2} zonePos={choiceIndex + 1} player={num}]";
			return new
			{
				instanceId = -entityId,
				name = name,
				path = $"/HsAuto/KerriganTurnStartChoice/{choiceIndex + 1}",
				activeInHierarchy = true,
				entity = SafeEntitySummary(obj)
			};
		}
		catch
		{
			return null;
		}
	}


	private static object TryDescribeObject(GameObject gameObject, bool includeComponents)
	{
		try
		{
			return Describe(gameObject, includeComponents);
		}
		catch
		{
			return null;
		}
	}


	private static bool TryGetGameObject(Component component, out GameObject gameObject)
	{
		gameObject = null;
		if ((Object)(object)component == (Object)null)
		{
			return false;
		}
		try
		{
			gameObject = component.gameObject;
			return (Object)(object)gameObject != (Object)null;
		}
		catch
		{
			gameObject = null;
			return false;
		}
	}


	private static bool TryGetInstanceId(GameObject gameObject, out int instanceId)
	{
		instanceId = 0;
		try
		{
			if ((Object)(object)gameObject == (Object)null)
			{
				return false;
			}
			instanceId = ((Object)gameObject).GetInstanceID();
			return true;
		}
		catch
		{
			instanceId = 0;
			return false;
		}
	}


	private static bool TryInvokeBool(object instance, string methodName)
	{
		try
		{
			object obj = InvokeNoArg(instance, methodName);
			bool flag = default;
			int num;
			if (obj is bool)
			{
				flag = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
		}
		catch
		{
			return false;
		}
	}


	private static Component FindComponentByName(GameObject gameObject, string typeName)
	{
		return (from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, typeName, StringComparison.Ordinal)) ?? (from component in gameObject.GetComponentsInChildren<Component>(true)
			where (Object)(object)component != (Object)null
			select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, typeName, StringComparison.Ordinal));
	}


	private static object GetInputManager()
	{
		Type type = FindLoadedType("InputManager");
		if (type == null)
		{
			return null;
		}
		return type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
	}


	private static Component FindMulliganManager()
	{
		return FindSingletonComponent("MulliganManager");
	}


	private static Component FindVisibleBattlegroundsEndGameScreen()
	{
		Type type = FindLoadedComponentType("BaconEndGameScreen");
		if (type == null)
		{
			return null;
		}
		return Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault((Component component) => TryGetGameObject(component, out var gameObject) && gameObject.activeInHierarchy);
	}


	private Component ProbeVisibleBattlegroundsEndGameScreen(bool force)
	{
		if ((Object)(object)_cachedVisibleBattlegroundsEndGameScreen != (Object)null && TryGetGameObject(_cachedVisibleBattlegroundsEndGameScreen, out var gameObject) && gameObject.activeInHierarchy)
		{
			return _cachedVisibleBattlegroundsEndGameScreen;
		}
		_cachedVisibleBattlegroundsEndGameScreen = null;
		float realtimeSinceStartup = Time.realtimeSinceStartup;
		if (!force && realtimeSinceStartup < _nextBattlegroundsEndGameProbeAt)
		{
			return null;
		}
		_nextBattlegroundsEndGameProbeAt = realtimeSinceStartup + 2f;
		_cachedVisibleBattlegroundsEndGameScreen = FindVisibleBattlegroundsEndGameScreen();
		return _cachedVisibleBattlegroundsEndGameScreen;
	}


	private static Component FindSingletonComponent(string typeName)
	{
		object obj = InvokeStaticNoArg(typeName, "Get");
		Component val = (Component)((obj is Component) ? obj : null);
		if (val != null && (Object)(object)val != (Object)null)
		{
			return val;
		}
		Type type = FindLoadedComponentType(typeName);
		if (!(type == null))
		{
			return Object.FindObjectsOfType(type).OfType<Component>().FirstOrDefault();
		}
		return null;
	}


	private static bool EnsureChosenEntityState(object entity, bool chosen, out string methodName, out string error)
	{
		methodName = "";
		error = null;
		try
		{
			object gameState = GetGameState();
			if (gameState == null || entity == null)
			{
				error = "GameState or Entity is unavailable.";
				return false;
			}
			if (TryReadChosenEntityState(gameState, entity, out var chosen2) && chosen2 == chosen)
			{
				methodName = "GameState.IsChosenEntity";
				return true;
			}
			string targetMethodName = (chosen ? "AddChosenEntity" : "RemoveChosenEntity");
			MethodInfo methodInfo = gameState.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, targetMethodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(entity);
			});
			if (methodInfo == null)
			{
				error = targetMethodName + "(Entity) not found.";
				return false;
			}
			methodInfo.Invoke(gameState, new object[1] { entity });
			methodName = gameState.GetType().FullName + "." + methodInfo.Name;
			bool chosen3;
			return !TryReadChosenEntityState(gameState, entity, out chosen3) || chosen3 == chosen;
		}
		catch (Exception ex)
		{
			error = ex.ToString();
			return false;
		}
	}


	private static bool TryInvokeSingleBoolArg(object instance, string methodName, bool value)
	{
		if (instance == null)
		{
			return false;
		}
		try
		{
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType == typeof(bool);
			});
			if (methodInfo == null)
			{
				return false;
			}
			methodInfo.Invoke(instance, new object[1] { value });
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool TryInvokeIntBoolArgs(object instance, string methodName, int intValue, bool boolValue)
	{
		if (instance == null)
		{
			return false;
		}
		try
		{
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 2 && CanPassInt(parameters[0].ParameterType) && parameters[1].ParameterType == typeof(bool);
			});
			if (methodInfo == null)
			{
				return false;
			}
			Type parameterType = methodInfo.GetParameters()[0].ParameterType;
			methodInfo.Invoke(instance, new object[2]
			{
				ConvertIntArgument(intValue, parameterType),
				boolValue
			});
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool TrySubmitMulliganChoices(object manager, int selectedHeroEntityId, IReadOnlyCollection<int> explicitKeptEntityIds, out object details, out string error)
	{
		details = null;
		error = null;
		try
		{
			object gameState = GetGameState();
			object obj = InvokeNoArg(gameState, "GetFriendlyEntityChoices");
			if (gameState == null || obj == null)
			{
				error = "GameState or friendly entity choices are unavailable.";
				return false;
			}
			object obj2 = InvokeNoArg(obj, "get_ID");
			if (obj2 == null)
			{
				error = "Friendly entity choice id is unavailable.";
				return false;
			}
			int num = Convert.ToInt32(obj2, CultureInfo.InvariantCulture);
			int num2 = Convert.ToInt32(InvokeNoArg(obj, "get_CountMin") ?? ((object)0), CultureInfo.InvariantCulture);
			int num3 = Convert.ToInt32(InvokeNoArg(obj, "get_CountMax") ?? ((object)int.MaxValue), CultureInfo.InvariantCulture);
			List<int> list;
			if (selectedHeroEntityId > 0)
			{
				list = new List<int> { selectedHeroEntityId };
			}
			else
			{
				list = ((explicitKeptEntityIds != null) ? explicitKeptEntityIds.Where((int entityId) => entityId > 0).Distinct().ToList() : ReadKeptMulliganEntityIds(manager));
			}
			if (list.Count < num2 || list.Count > num3)
			{
				error = $"Kept mulligan choice count {list.Count} is outside [{num2}, {num3}].";
				details = new
				{
					choiceId = num,
					countMin = num2,
					countMax = num3,
					keptEntityIds = list
				};
				return false;
			}
			object obj3 = (FindLoadedType("Network")?.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null))?.Invoke(null, null);
			if (obj3 == null)
			{
				error = "Network.Get() returned null.";
				return false;
			}
			MethodInfo methodInfo = obj3.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, "SendChoices", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 2 && CanPassInt(parameters[0].ParameterType) && parameters[1].ParameterType.IsAssignableFrom(typeof(List<int>));
			});
			if (methodInfo == null)
			{
				error = "Network.SendChoices(int, List<int>) not found.";
				return false;
			}
			methodInfo.Invoke(obj3, new object[2]
			{
				ConvertIntArgument(num, methodInfo.GetParameters()[0].ParameterType),
				list
			});
			InvokeNoArg(gameState, "ClearResponseMode");
			details = new
			{
				choiceId = num,
				countMin = num2,
				countMax = num3,
				keptEntityIds = list
			};
			return true;
		}
		catch (Exception ex)
		{
			error = ex.ToString();
			return false;
		}
	}


	private static List<int> ReadKeptMulliganEntityIds(object manager)
	{
		List<int> list = new List<int>();
		FieldInfo? fieldInfo = manager?.GetType().GetField("m_handCardsMarkedForReplace", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		FieldInfo fieldInfo2 = manager?.GetType().GetField("m_startingCards", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (!(fieldInfo?.GetValue(manager) is Array array) || !(fieldInfo2?.GetValue(manager) is IList list2))
		{
			return list;
		}
		int num = Math.Min(array.Length, list2.Count);
		bool flag = default;
		for (int i = 0; i < num; i++)
		{
			object value = array.GetValue(i);
			int num2;
			if (value is bool)
			{
				flag = (bool)value;
				num2 = 1;
			}
			else
			{
				num2 = 0;
			}
			if (((uint)num2 & (flag ? 1u : 0u)) != 0)
			{
				continue;
			}
			object obj = InvokeNoArg(InvokeNoArg(list2[i], "GetEntity"), "GetEntityId");
			if (obj != null)
			{
				int item = Convert.ToInt32(obj, CultureInfo.InvariantCulture);
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		return list;
	}


	private static bool TryReadChosenEntityState(object gameState, object entity, out bool chosen)
	{
		chosen = false;
		try
		{
			MethodInfo methodInfo = gameState.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, "IsChosenEntity", StringComparison.Ordinal) || item.ReturnType != typeof(bool))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(entity);
			});
			if (methodInfo == null)
			{
				return false;
			}
			object obj = methodInfo.Invoke(gameState, new object[1] { entity });
			bool flag = default;
			int num;
			if (obj is bool)
			{
				flag = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			chosen = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool? TryDoNetworkResponse(object inputManager, object entity, bool checkValidInput, bool wantDeckActionOption, bool wantDisguisedOption, out string methodName)
	{
		return TryDoNetworkResponse(inputManager, entity, checkValidInput, wantDeckActionOption, wantDisguisedOption, wantInteractableObject: false, out methodName);
	}


	private static bool? TryDoNetworkResponse(object inputManager, object entity, bool checkValidInput, bool wantDeckActionOption, bool wantDisguisedOption, bool wantInteractableObject, out string methodName)
	{
		methodName = "";
		if (inputManager == null || entity == null)
		{
			return null;
		}
		bool flag = default;
		foreach (MethodInfo item in from item in inputManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			where string.Equals(item.Name, "DoNetworkResponse", StringComparison.Ordinal)
			select item)
		{
			ParameterInfo[] parameters = item.GetParameters();
			object[] parameters2;
			if (parameters.Length == 3 && parameters[0].ParameterType.IsInstanceOfType(entity) && parameters[1].ParameterType == typeof(bool) && parameters[2].ParameterType.IsEnum && string.Equals(parameters[2].ParameterType.Name, "GAME_TAG", StringComparison.Ordinal))
			{
				string text;
				if (wantDisguisedOption)
				{
					text = "DISGUISED";
				}
				else if (wantDeckActionOption)
				{
					text = "DECK_ACTION_COST";
				}
				else
				{
					text = (wantInteractableObject ? "INTERACTABLE_OBJECT" : null);
				}
				object obj;
				try
				{
					obj = ((text == null) ? Enum.ToObject(parameters[2].ParameterType, 0) : Enum.Parse(parameters[2].ParameterType, text, ignoreCase: false));
				}
				catch
				{
					obj = Enum.ToObject(parameters[2].ParameterType, 0);
				}
				parameters2 = new object[3] { entity, checkValidInput, obj };
			}
			else if (parameters.Length == 5 && parameters[0].ParameterType.IsInstanceOfType(entity) && parameters.Skip(1).All((ParameterInfo parameter) => parameter.ParameterType == typeof(bool)))
			{
				parameters2 = new object[5] { entity, checkValidInput, wantDeckActionOption, wantDisguisedOption, wantInteractableObject };
			}
			else if (parameters.Length == 4 && parameters[0].ParameterType.IsInstanceOfType(entity) && parameters.Skip(1).All((ParameterInfo parameter) => parameter.ParameterType == typeof(bool)))
			{
				parameters2 = new object[4] { entity, checkValidInput, wantDeckActionOption, wantDisguisedOption };
			}
			else
			{
				if (parameters.Length != 3 || !parameters[0].ParameterType.IsInstanceOfType(entity) || !parameters.Skip(1).All((ParameterInfo parameter) => parameter.ParameterType == typeof(bool)))
				{
					continue;
				}
				parameters2 = new object[3] { entity, checkValidInput, wantDeckActionOption };
			}
			methodName = ((parameters.Length == 3 && parameters[2].ParameterType.IsEnum) ? $"{item.Name}/{parameters.Length}/{parameters[2].ParameterType.Name}" : $"{item.Name}/{parameters.Length}");

			try
			{
				object obj3 = item.Invoke(inputManager, parameters2);
				int num;
				if (obj3 is bool)
				{
					flag = (bool)obj3;
					num = 1;
				}
				else
				{
					num = 0;
				}
				return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
			}
			finally
			{

			}
		}
		return null;
	}


	private static bool TryHandleClickOnSubOption(object inputManager, object entity, out string methodName)
	{
		methodName = "";
		if (inputManager == null || entity == null)
		{
			return false;
		}
		MethodInfo methodInfo = inputManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, "HandleClickOnSubOption", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 3 && parameters[0].ParameterType.IsInstanceOfType(entity) && parameters[1].ParameterType == typeof(bool) && parameters[2].ParameterType == typeof(bool);
		});
		if (methodInfo == null)
		{
			return false;
		}

		try
		{
			methodInfo.Invoke(inputManager, new object[3] { entity, false, true });
			methodName = methodInfo.Name + "/3";
			return true;
		}
		finally
		{

		}
	}


	private static bool TryHandleClickOnChoice(object inputManager, object entity, out string methodName)
	{
		methodName = "";
		if (inputManager == null || entity == null)
		{
			return false;
		}
		MethodInfo methodInfo = (from item in inputManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where((MethodInfo item) =>
			{
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(entity) && (string.Equals(item.Name, "DoNetworkChoice", StringComparison.Ordinal) || string.Equals(item.Name, "HandleClickOnChoice", StringComparison.Ordinal));
			})
			orderby (!string.Equals(item.Name, "HandleClickOnChoice", StringComparison.Ordinal)) ? 1 : 0
			select item).ToArray().FirstOrDefault();
		if (methodInfo == null)
		{
			return false;
		}

		try
		{
			methodInfo.Invoke(inputManager, new object[1] { entity });
			methodName = methodInfo.Name + "/1";
			return true;
		}
		finally
		{

		}
	}


	private static bool TryClearFriendlyChosenEntities(object gameState, out string methodName)
	{
		methodName = "";
		if (gameState == null)
		{
			return false;
		}
		MethodInfo[] methods = gameState.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo methodInfo = methods.FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "GetChosenEntities", StringComparison.Ordinal) && item.GetParameters().Length == 0);
		MethodInfo methodInfo2 = methods.FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "RemoveChosenEntity", StringComparison.Ordinal) && item.GetParameters().Length == 1);
		if (methodInfo == null || methodInfo2 == null)
		{
			return false;
		}
		if (!(methodInfo.Invoke(gameState, null) is IEnumerable source))
		{
			return false;
		}
		object[] array = (from object item in source
			where item != null
			select item).ToArray();
		object[] array2 = array;
		foreach (object obj in array2)
		{
			if (!methodInfo2.GetParameters()[0].ParameterType.IsInstanceOfType(obj))
			{
				return false;
			}
			object obj2 = methodInfo2.Invoke(gameState, new object[1] { obj });
			if (obj2 is bool && !(bool)obj2)
			{
				return false;
			}
		}
		methodName = $"{methodInfo.Name}/0 + {methodInfo2.Name}/1 x{array.Length}";
		return true;
	}


	private static bool TrySubmitExactFriendlyChoice(object gameState, object entity, out string methodName)
	{
		return TrySubmitExactFriendlyChoices(gameState, new object[1] { entity }, out methodName);
	}


	private static bool TrySubmitExactFriendlyChoices(object gameState, IReadOnlyList<object> entities, out string methodName)
	{
		methodName = "";
		if (gameState == null || entities == null || entities.Count == 0 || entities.Any((object entity) => entity == null))
		{
			return false;
		}
		MethodInfo[] methods = gameState.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		MethodInfo methodInfo = methods.FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "GetFriendlyEntityChoices", StringComparison.Ordinal) && item.GetParameters().Length == 0);
		MethodInfo methodInfo2 = methods.FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "ClearFriendlyChoicesList", StringComparison.Ordinal) && item.GetParameters().Length == 0);
		MethodInfo methodInfo3 = methods.FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, "AddChosenEntity", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && entities.All(parameters[0].ParameterType.IsInstanceOfType);
		});
		MethodInfo methodInfo4 = methods.FirstOrDefault((MethodInfo item) => string.Equals(item.Name, "SendChoices", StringComparison.Ordinal) && item.GetParameters().Length == 0);
		if (methodInfo == null || methodInfo2 == null || methodInfo3 == null || methodInfo4 == null)
		{
			return false;
		}
		object friendlyChoices = methodInfo.Invoke(gameState, null);
		if (friendlyChoices == null)
		{
			return false;
		}
		MethodInfo methodInfo5 = methods.FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, "EnterChoiceMode", StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(friendlyChoices);
		});
		if (methodInfo5 == null)
		{
			return false;
		}
		methodInfo5.Invoke(gameState, new object[1] { friendlyChoices });
		methodInfo2.Invoke(gameState, null);
		foreach (object entity in entities)
		{
			object obj = methodInfo3.Invoke(gameState, new object[1] { entity });
			if (obj is bool && !(bool)obj)
			{
				return false;
			}
		}

		try
		{
			methodInfo4.Invoke(gameState, null);
			methodName = $"{methodInfo5.Name}/1 + {methodInfo2.Name}/0 + {methodInfo3.Name}/1 x{entities.Count} + {methodInfo4.Name}/0";
			return true;
		}
		finally
		{

		}
	}


	private static int[] ReadFriendlyTargetChoiceEntityIds(object gameState, out int choiceId, out int countMin, out int countMax)
	{
		int[] result = ReadFriendlyChoiceEntityIds(gameState, out var choiceType, out choiceId, out countMin, out countMax);
		if (string.Equals(choiceType, "TARGET", StringComparison.OrdinalIgnoreCase))
		{
			return result;
		}
		choiceId = 0;
		countMin = 0;
		countMax = 0;
		return Array.Empty<int>();
	}


	private static int[] ReadFriendlyKerriganChoiceEntityIds(object gameState, out int choiceId, out int countMin, out int countMax)
	{
		int[] array = ReadFriendlyChoiceEntityIds(gameState, out var choiceType, out choiceId, out countMin, out countMax);
		if (!string.Equals(choiceType, "GENERAL", StringComparison.OrdinalIgnoreCase) || array.Length == 0)
		{
			choiceId = 0;
			countMin = 0;
			countMax = 0;
			return Array.Empty<int>();
		}
		int[] array2 = array;
		foreach (int value in array2)
		{
			if (!(InvokeNoArg(InvokeIntArgObject(gameState, "GetEntity", value) ?? InvokeIntArgObject(gameState, "GetEntityByID", value), "GetCardId")?.ToString() ?? "").StartsWith("BG31_HERO_811t", StringComparison.OrdinalIgnoreCase))
			{
				choiceId = 0;
				countMin = 0;
				countMax = 0;
				return Array.Empty<int>();
			}
		}
		return array;
	}


	private static int[] ReadFriendlyChoiceEntityIds(object gameState, out string choiceType, out int choiceId, out int countMin, out int countMax)
	{
		choiceType = "";
		choiceId = 0;
		countMin = 0;
		countMax = 0;
		if (gameState == null || !TryInvokeBool(gameState, "IsInChoiceMode"))
		{
			return Array.Empty<int>();
		}
		try
		{
			object obj = InvokeNoArg(gameState, "GetFriendlyEntityChoices");
			choiceType = InvokeNoArg(obj, "get_ChoiceType")?.ToString() ?? "";
			if (obj == null)
			{
				return Array.Empty<int>();
			}
			choiceId = Convert.ToInt32(InvokeNoArg(obj, "get_ID") ?? ((object)0), CultureInfo.InvariantCulture);
			countMin = Convert.ToInt32(InvokeNoArg(obj, "get_CountMin") ?? ((object)0), CultureInfo.InvariantCulture);
			countMax = Convert.ToInt32(InvokeNoArg(obj, "get_CountMax") ?? ((object)0), CultureInfo.InvariantCulture);
			HashSet<int> unchoosable = new HashSet<int>(ReadChoiceEntityIds(InvokeNoArg(obj, "get_UnchoosableEntities") as IEnumerable));
			return (from entityId in ReadChoiceEntityIds(InvokeNoArg(obj, "get_Entities") as IEnumerable)
				where !unchoosable.Contains(entityId)
				select entityId).Distinct().Take(32).ToArray();
		}
		catch
		{
			choiceType = "";
			choiceId = 0;
			countMin = 0;
			countMax = 0;
			return Array.Empty<int>();
		}
	}


	private static IEnumerable<int> ReadChoiceEntityIds(IEnumerable values)
	{
		if (values == null)
		{
			yield break;
		}
		foreach (object value in values)
		{
			int num;
			try
			{
				num = Convert.ToInt32(value, CultureInfo.InvariantCulture);
			}
			catch
			{
				continue;
			}
			if (num > 0)
			{
				yield return num;
			}
		}
	}


	private static object GetGameState()
	{
		Type type = FindLoadedType("GameState");
		if (type == null)
		{
			return null;
		}
		return type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(null, null);
	}


	private static bool IsActiveGameInProgress()
	{
		object gameState = GetGameState();
		object obj = InvokeNoArg(gameState, "GetGameEntity");
		if (gameState == null || obj == null || TryInvokeBool(gameState, "IsGameOver"))
		{
			return false;
		}
		object obj2 = InvokeNoArg(gameState, "GetFriendlySidePlayer") ?? InvokeNoArg(gameState, "GetLocalSidePlayer");
		int valueOrDefault = ReadEntityTagInt(obj2, "PLAYSTATE").GetValueOrDefault();
		if (((uint)(valueOrDefault - 4) <= 2u || valueOrDefault == 8) ? true : false)
		{
			return false;
		}
		if (obj2 == null)
		{
			return ReadIntMember(gameState, "GetFriendlyPlayerId", 0) > 0;
		}
		return true;
	}


	private static object GetDeckPickerTrayDisplay()
	{
		object obj = InvokeStaticNoArg("DeckPickerTrayDisplay", "Get");
		if (obj != null)
		{
			return obj;
		}
		return (from component in Object.FindObjectsOfType<Component>()
			where (Object)(object)component != (Object)null
			select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, "DeckPickerTrayDisplay", StringComparison.Ordinal));
	}


	private static ConstructedDeckScanResult ScanConstructedDeckPages(string requestedName, int? requestedIndex, bool stopAtMatch, bool restoreOriginalPage)
	{
		ConstructedDeckScanResult result = new ConstructedDeckScanResult();
		if (IsActiveGameInProgress())
		{
			return result;
		}
		object deckPickerTrayDisplay = GetDeckPickerTrayDisplay();
		result.LeftClaimedLoanerPage = LeaveClaimedLoanerDeckPage(deckPickerTrayDisplay);
		if (result.LeftClaimedLoanerPage)
		{
			return result;
		}
		if (deckPickerTrayDisplay != null && !TryInvokeBool(deckPickerTrayDisplay, "CustomPagesReady"))
		{
			return result;
		}
		result.OriginalPageIndex = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
		result.FinalPageIndex = result.OriginalPageIndex;
		if (deckPickerTrayDisplay == null)
		{
			result.Decks.AddRange(ReadConstructedDeckCandidates(-1, 0));
			result.Target = FindRequestedConstructedDeck(result.Decks, requestedName, requestedIndex);
			return result;
		}
		for (int i = 0; i < 64; i++)
		{
			if (TryInvokeBool(deckPickerTrayDisplay, "IsShowingFirstPage"))
			{
				break;
			}
			int num = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
			if (!TryInvokeSingleBoolArg(deckPickerTrayDisplay, "ShowPreviousPage", value: true))
			{
				break;
			}
			int num2 = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
			if (num2 < 0 || num2 == num)
			{
				break;
			}
			result.UsedInternalPaging = true;
		}
		HashSet<int> hashSet = new HashSet<int>();
		for (int j = 0; j < 64; j++)
		{
			int num3 = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
			if (!hashSet.Add(num3))
			{
				break;
			}
			result.VisitedPageIndexes.Add(num3);
			ConstructedDeckCandidate[] array = (from deck in ReadConstructedDeckCandidates(num3, j)
				orderby deck.Index
				select deck).Select((ConstructedDeckCandidate deck, int index) =>
			{
				deck.GlobalIndex = result.Decks.Count + index;
				return deck;
			}).ToArray();
			result.Decks.AddRange(array);
			ConstructedDeckCandidate constructedDeckCandidate = FindRequestedConstructedDeck(array, requestedName, requestedIndex);
			if (constructedDeckCandidate != null)
			{
				result.Target = constructedDeckCandidate;
				if (stopAtMatch)
				{
					break;
				}
			}
			if (TryInvokeBool(deckPickerTrayDisplay, "IsShowingLastPage") || TryInvokeBool(deckPickerTrayDisplay, "IsNextPageDisabled"))
			{
				result.ReachedLastPage = true;
				break;
			}
			int num4 = num3;
			if (!TryInvokeSingleBoolArg(deckPickerTrayDisplay, "ShowNextPage", value: true))
			{
				break;
			}
			int num5 = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
			if (num5 < 0 || num5 == num4)
			{
				break;
			}
			result.UsedInternalPaging = true;
		}
		result.FinalPageIndex = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
		if (restoreOriginalPage && result.OriginalPageIndex >= 0 && result.FinalPageIndex != result.OriginalPageIndex && TryInvokeIntBoolArgs(deckPickerTrayDisplay, "ShowPage", result.OriginalPageIndex, boolValue: true))
		{
			result.RestoredOriginalPage = true;
			result.FinalPageIndex = ReadMemberAsInt(deckPickerTrayDisplay, "m_currentPageIndex", -1);
		}
		return result;
	}


	private static object ToConstructedDeckPaginationPayload(ConstructedDeckScanResult scan)
	{
		return new
		{
			originalPageIndex = scan.OriginalPageIndex,
			leftClaimedLoanerPage = scan.LeftClaimedLoanerPage,
			finalPageIndex = scan.FinalPageIndex,
			visitedPageIndexes = scan.VisitedPageIndexes.ToArray(),
			visitedPageCount = scan.VisitedPageIndexes.Count,
			reachedLastPage = scan.ReachedLastPage,
			usedInternalPaging = scan.UsedInternalPaging,
			restoredOriginalPage = scan.RestoredOriginalPage,
			method = "DeckPickerTrayDisplay.ShowPreviousPage(bool)+ShowNextPage(bool)+ShowPage(int,bool)"
		};
	}


	private static IEnumerable<ConstructedDeckCandidate> ReadConstructedDeckCandidates(int pageIndex, int pageOrdinal)
	{
		Type visualType = FindLoadedComponentType("CollectionDeckBoxVisual");
		Component[] visibleVisuals = ((visualType == null) ? Array.Empty<Component>() : Object.FindObjectsOfType(visualType).OfType<Component>().ToArray());
		HashSet<int> seen = new HashSet<int>();
		foreach (GameObject gameObject in EnumerateObjects(sort: false))
		{
			Match match = Regex.Match(((Object)gameObject).name ?? "", "^CollectionDeck\\(Clone\\)\\s*-\\s*(?<index>\\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
			if (!match.Success || !TryGetInstanceId(gameObject, out var instanceId) || !seen.Add(instanceId))
			{
				continue;
			}
			string path = GetPath(gameObject);
			if (path.IndexOf("DeckPicker", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("DeckTray", StringComparison.OrdinalIgnoreCase) < 0 && path.IndexOf("Tournament", StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}
			int num = (int.TryParse(match.Groups["index"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : (-1));
			Component val = (from component in gameObject.GetComponentsInChildren<Component>(true)
				where (Object)(object)component != (Object)null
				select component).FirstOrDefault((Component component) => (!(visualType != null)) ? string.Equals(((object)component).GetType().Name, "CollectionDeckBoxVisual", StringComparison.Ordinal) : visualType.IsAssignableFrom(((object)component).GetType())) ?? (from component in visibleVisuals
				where (Object)(object)component != (Object)null && (Object)(object)component.gameObject != (Object)null
				where component.transform.IsChildOf(gameObject.transform) || gameObject.transform.IsChildOf(component.transform)
				orderby GetPath(component.gameObject).Length descending
				select component).FirstOrDefault();
			object obj = InvokeNoArg(val, "GetCollectionDeck");
			object obj2 = InvokeNoArg(obj, "get_IsLoanerDeck");
			if (!(obj2 is bool) || !(bool)obj2 || (ReadIntValue(InvokeNoArg(InvokeStaticNoArg("FreeDeckMgr", "Get"), "get_Status"), 0) != 2 && ReadIntValue(InvokeNoArg(InvokeStaticNoArg("FreeDeckMgr", "Get"), "get_ClaimedDeckTemplateId"), 0) <= 0))
			{
				string text = InvokeNoArg(obj, "get_Name")?.ToString() ?? ReadConstructedDeckName(gameObject);
				if (obj != null && !string.IsNullOrWhiteSpace(text))
				{
					yield return new ConstructedDeckCandidate
					{
						GameObject = gameObject,
						Visual = val,
						Index = num,
						GlobalIndex = pageOrdinal * 9 + Math.Max(0, num),
						PageIndex = pageIndex,
						PageOrdinal = pageOrdinal,
						Name = text,
						NormalizedName = NormalizeConstructedDeckName(text),
						Path = path,
						InstanceId = instanceId,
						Active = gameObject.activeInHierarchy,
						Selected = IsConstructedDeckSelected(gameObject, val)
					};
				}
			}
		}
	}


	private static ConstructedDeckCandidate FindRequestedConstructedDeck(IReadOnlyList<ConstructedDeckCandidate> decks, string requestedName, int? requestedIndex)
	{
		if (!string.IsNullOrWhiteSpace(requestedName))
		{
			return decks.FirstOrDefault((ConstructedDeckCandidate deck) => string.Equals(deck.NormalizedName, requestedName, StringComparison.OrdinalIgnoreCase));
		}
		if (requestedIndex.HasValue)
		{
			return decks.FirstOrDefault((ConstructedDeckCandidate deck) => deck.GlobalIndex == requestedIndex.Value);
		}
		if (!string.IsNullOrWhiteSpace(requestedName))
		{
			return null;
		}
		return decks.FirstOrDefault();
	}


	private static object ToConstructedDeckPayload(ConstructedDeckCandidate deck)
	{
		return new
		{
			index = deck.GlobalIndex,
			displayIndex = deck.GlobalIndex + 1,
			slotIndex = deck.Index,
			pageIndex = deck.PageIndex,
			pageDisplayIndex = deck.PageOrdinal + 1,
			name = deck.Name,
			normalizedName = deck.NormalizedName,
			path = deck.Path,
			instanceId = deck.InstanceId,
			active = deck.Active,
			selected = deck.Selected
		};
	}


	private static string ReadConstructedDeckName(GameObject deckObject)
	{
		foreach (Component item in from component in deckObject.GetComponentsInChildren<Component>(true)
			where (Object)(object)component != (Object)null
			select component)
		{
			if (TryGetGameObject(item, out var gameObject) && string.Equals(((Object)gameObject).name, "DeckName", StringComparison.OrdinalIgnoreCase))
			{
				string text = NormalizeConstructedDeckName(ReadLikelyText(item));
				if (!string.IsNullOrWhiteSpace(text))
				{
					return text;
				}
			}
		}
		foreach (Component item2 in from component in deckObject.GetComponentsInChildren<Component>(true)
			where (Object)(object)component != (Object)null
			select component)
		{
			string text2 = NormalizeConstructedDeckName(ReadLikelyText(item2));
			if (!string.IsNullOrWhiteSpace(text2) && !IsConstructedDeckUiText(text2))
			{
				return text2;
			}
		}
		return "";
	}


	private static bool IsConstructedDeckSelected(GameObject deckObject, Component visual)
	{
		if (IsSameUnityObject(ReadMember(GetDeckPickerTrayDisplay(), "m_selectedCustomDeckBox"), visual, deckObject))
		{
			return true;
		}
		if ((Object)(object)visual != (Object)null)
		{
			if (!TryInvokeBool(visual, "IsSelected"))
			{
				return ReadMemberAsBool(visual, "m_isSelected", defaultValue: false);
			}
			return true;
		}
		return false;
	}


	private static string TrySelectConstructedDeckInternally(ConstructedDeckCandidate deck)
	{
		if ((Object)(object)deck?.Visual == (Object)null)
		{
			return null;
		}
		object deckPickerTrayDisplay = GetDeckPickerTrayDisplay();
		string[] array = new string[3] { "SelectCustomDeck", "OnCustomDeckPressed", "UpdateDeckVisualsAndSelectDeck" };
		foreach (string methodName in array)
		{
			string text = TryInvokeObjectArgMethod(deckPickerTrayDisplay, methodName, deck.Visual);
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		string text2 = TryInvokeZeroArgComponentMethod(deck.Visual, "OnPress");
		string text3 = TryInvokeZeroArgComponentMethod(deck.Visual, "OnRelease");
		if (!string.IsNullOrWhiteSpace(text2) && !string.IsNullOrWhiteSpace(text3))
		{
			return text2 + "+" + text3;
		}
		return null;
	}


	private static string NormalizeConstructedDeckName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		if (Regex.IsMatch(value, "\\\\u[0-9a-fA-F]{4}"))
		{
			try
			{
				value = Regex.Unescape(value);
			}
			catch
			{
			}
		}
		string input = Regex.Replace(value.Replace("\r", " ").Replace("\n", " "), "<.*?>", " ");
		input = Regex.Replace(input, "\\s+", " ").Trim();
		if (input.Length > 64)
		{
			return "";
		}
		return input;
	}


	private static bool IsConstructedDeckUiText(string value)
	{
		if (!Regex.IsMatch(value ?? "", "^\\d+\\s*/\\s*\\d+$", RegexOptions.CultureInvariant))
		{
			return ContainsAny(value, "DeckPicker", "DeckTray", "Button", "Root", "Visual", "标准模式", "狂野模式", "休闲模式", "我的收藏", "开始");
		}
		return true;
	}


	private static object DescribeConstructedEndTurnButton(Component endTurnButton)
	{
		bool hasEndTurnOption = TryGetEndTurnOption(out var optionIndex);
		if ((Object)(object)endTurnButton == (Object)null)
		{
			return new
			{
				exists = false,
				hasEndTurnOption = hasEndTurnOption,
				optionIndex = optionIndex,
				isDisabled = true,
				inputBlockedInternally = true,
				isInputBlocked = true,
				isInWaitingState = false,
				isInNmpState = false,
				isInYouHavePlaysState = false,
				hasNoMorePlays = false
			};
		}
		return new
		{
			exists = true,
			hasEndTurnOption = hasEndTurnOption,
			optionIndex = optionIndex,
			isDisabled = ReadMemberAsBool(endTurnButton, "IsDisabled", defaultValue: false),
			inputBlockedInternally = ReadMemberAsBool(endTurnButton, "InputBlockedInternally", defaultValue: false),
			isInputBlocked = TryInvokeBool(endTurnButton, "IsInputBlocked"),
			isInWaitingState = TryInvokeBool(endTurnButton, "IsInWaitingState"),
			isInNmpState = TryInvokeBool(endTurnButton, "IsInNMPState"),
			isInYouHavePlaysState = TryInvokeBool(endTurnButton, "IsInYouHavePlaysState"),
			hasNoMorePlays = TryInvokeBool(endTurnButton, "HasNoMorePlays")
		};
	}


	private static bool TryGetEndTurnOption(out int optionIndex)
	{
		optionIndex = -1;
		if (!(InvokeNoArg(InvokeNoArg(GetGameState(), "GetOptionsPacket"), "get_List") is IEnumerable optionList))
		{
			return false;
		}
		if (TryFindEndTurnOption(optionList, out var selectedIndex, out var _, out var _, out var _))
		{
			optionIndex = selectedIndex;
			return true;
		}
		return false;
	}


	private static bool TryFindEndTurnOption(IEnumerable optionList, out int selectedIndex, out int selectedType, out string selectedTypeName, out List<object> candidates)
	{
		selectedIndex = -1;
		selectedType = int.MinValue;
		selectedTypeName = "";
		candidates = new List<object>();
		int num = -1;
		foreach (object option in optionList)
		{
			num++;
			if (option != null)
			{
				int num2 = ReadIntMember(option, "get_Type", int.MinValue);
				string text = InvokeNoArg(option, "get_Type")?.ToString() ?? "";
				object instance = InvokeNoArg(option, "get_Main");
				object playError = DescribePlayErrorInfo(InvokeNoArg(instance, "get_PlayErrorInfo"));
				candidates.Add(new
				{
					index = num,
					type = num2,
					typeName = text,
					mainEntityId = ReadIntMember(instance, "get_ID", 0),
					playError = playError
				});
				if (num2 == 2 || string.Equals(text, "END_TURN", StringComparison.OrdinalIgnoreCase))
				{
					selectedIndex = num;
					selectedType = num2;
					selectedTypeName = text;
					return true;
				}
			}
		}
		return false;
	}


	private static void TryNotifyEndTurnButtonRequested()
	{
		try
		{
			InvokeNoArg((from component in Object.FindObjectsOfType<Component>()
				where (Object)(object)component != (Object)null
				select component).FirstOrDefault((Component component) => string.Equals(((object)component).GetType().Name, "EndTurnButton", StringComparison.Ordinal)), "OnEndTurnRequested");
		}
		catch
		{
		}
	}


	private static int ToConstructedVisualsFormatType(string format)
	{
		string text = (format ?? "").Trim();
		if (text.Equals("wild", StringComparison.OrdinalIgnoreCase) || text.Contains("狂野"))
		{
			return 1;
		}
		if (text.Equals("casual", StringComparison.OrdinalIgnoreCase) || text.Contains("休闲"))
		{
			return 4;
		}
		if (text.Equals("standard", StringComparison.OrdinalIgnoreCase) || text.Contains("标准") || string.IsNullOrWhiteSpace(text))
		{
			return 2;
		}
		return 0;
	}


	private static bool IsSupportedConstructedVisualsFormatType(int value)
	{
		if (value != 1 && value != 2)
		{
			return value == 4;
		}
		return true;
	}


	private static bool TryInvokeVisualsFormatTypeArg(object instance, string methodName, int value, out string methodNameWithSignature, out string error)
	{
		methodNameWithSignature = "";
		error = "";
		if (instance == null)
		{
			error = "Target instance is null.";
			return false;
		}
		try
		{
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && (parameters[0].ParameterType.Name.Equals("VisualsFormatType", StringComparison.Ordinal) || CanPassInt(parameters[0].ParameterType));
			});
			if (methodInfo == null)
			{
				error = methodName + "(VisualsFormatType) was not found.";
				return false;
			}
			Type parameterType = methodInfo.GetParameters()[0].ParameterType;
			methodInfo.Invoke(instance, new object[1] { ConvertIntArgument(value, parameterType) });
			methodNameWithSignature = instance.GetType().Name + "." + methodInfo.Name + "(" + parameterType.Name + ")";
			return true;
		}
		catch (Exception ex)
		{
			error = FlattenInvocationException(ex);
			return false;
		}
	}


	private static object DescribeConstructedFormatState(object tray)
	{
		return new
		{
			currentVisualsFormatType = (ReadMemberAsString(tray, "m_visualsFormatType") ?? ""),
			isModeSwitchShowing = ReadMemberAsBool(tray, "IsModeSwitchShowing", defaultValue: false),
			optionsFormatType = (InvokeStaticNoArg("Options", "GetFormatType")?.ToString() ?? ""),
			optionsInRankedPlayMode = (InvokeStaticNoArg("Options", "GetInRankedPlayMode")?.ToString() ?? "")
		};
	}


	private static string FlattenInvocationException(Exception ex)
	{
		if (!(ex is TargetInvocationException) || ex.InnerException == null)
		{
			return ex.ToString();
		}
		return ex.InnerException.ToString();
	}


	private static object InvokeStaticNoArg(string typeName, string methodName)
	{
		Type type = FindLoadedType(typeName);
		if (type == null)
		{
			return null;
		}
		return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(null, null);
	}


	private static float ReadTurnTimerRemainingSeconds(object turnTimer)
	{
		if (turnTimer == null)
		{
			return -1f;
		}
		try
		{
			object obj = InvokeNoArg(turnTimer, "ComputeCountdownRemainingSec");
			return (obj == null) ? (-1f) : Math.Max(-1f, Convert.ToSingle(obj, CultureInfo.InvariantCulture));
		}
		catch
		{
			return -1f;
		}
	}


	private static object BuildRegionDiagnostics()
	{
		return new
		{
			environmentRegion = (Environment.GetEnvironmentVariable("REGION") ?? "").Trim(),
			launchOptionRegion = ReadBattleNetLaunchRegion(),
			currentRegion = ReadStaticPropertyAsString("Hearthstone.Util.RegionUtils", "CurrentRegion"),
			accountRegion = InvokeStaticAsString("BnetUtils", "TryGetBnetRegion"),
			gameRegion = InvokeStaticAsString("BnetUtils", "TryGetGameRegion")
		};
	}


	private static string ReadBattleNetLaunchRegion()
	{
		try
		{
			return ((FindLoadedType("Blizzard.GameService.SDK.Client.Integration.BattleNet") ?? FindLoadedType("BattleNet"))?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, "GetLaunchOption", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(bool);
			}))?.Invoke(null, new object[2] { "REGION", false })?.ToString() ?? "";
		}
		catch
		{
			return "";
		}
	}


	private static string ReadStaticPropertyAsString(string typeName, string propertyName)
	{
		try
		{
			return FindLoadedType(typeName)?.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null, null)?.ToString() ?? "";
		}
		catch
		{
			return "";
		}
	}


	private static string InvokeStaticAsString(string typeName, string methodName)
	{
		try
		{
			return InvokeStaticNoArg(typeName, methodName)?.ToString() ?? "";
		}
		catch
		{
			return "";
		}
	}


	private static object InvokeStaticIntArg(string typeName, string methodName, int value)
	{
		Type type = FindLoadedType(typeName);
		if (type == null)
		{
			return null;
		}
		MethodInfo methodInfo = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && CanPassInt(parameters[0].ParameterType);
		});
		if (methodInfo == null)
		{
			return null;
		}
		Type parameterType = methodInfo.GetParameters()[0].ParameterType;
		return methodInfo.Invoke(null, new object[1] { ConvertIntArgument(value, parameterType) });
	}


	private static object InvokeStaticStringEnumArg(string typeName, string methodName, string stringValue, int enumValue)
	{
		Type type = FindLoadedType(typeName);
		if (type == null)
		{
			return null;
		}
		MethodInfo methodInfo = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 2 && parameters[0].ParameterType == typeof(string) && CanPassInt(parameters[1].ParameterType);
		});
		if (methodInfo == null)
		{
			return null;
		}
		Type parameterType = methodInfo.GetParameters()[1].ParameterType;
		return methodInfo.Invoke(null, new object[2]
		{
			stringValue,
			ConvertIntArgument(enumValue, parameterType)
		});
	}


	private static object InvokeEnumArg(object instance, string methodName, int enumValue)
	{
		if (instance == null)
		{
			return null;
		}
		MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && CanPassInt(parameters[0].ParameterType);
		});
		if (methodInfo == null)
		{
			return null;
		}
		Type parameterType = methodInfo.GetParameters()[0].ParameterType;
		return methodInfo.Invoke(instance, new object[1] { ConvertIntArgument(enumValue, parameterType) });
	}


	private static Type FindLoadedType(string fullNameOrName)
	{
		if (LoadedTypeCache.TryGetValue(fullNameOrName, out var value))
		{
			return value;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type = null;
			try
			{
				type = assembly.GetType(fullNameOrName, throwOnError: false);
				if (type != null)
				{
					LoadedTypeCache[fullNameOrName] = type;
					return type;
				}
				type = assembly.GetTypes().FirstOrDefault((Type type2) => string.Equals(type2.FullName, fullNameOrName, StringComparison.Ordinal) || string.Equals(type2.Name, fullNameOrName, StringComparison.Ordinal));
			}
			catch
			{
			}
			if (type != null)
			{
				LoadedTypeCache[fullNameOrName] = type;
				return type;
			}
		}
		return null;
	}


	private static Type FindLoadedComponentType(string fullNameOrName)
	{
		string key = "component::" + fullNameOrName;
		if (LoadedTypeCache.TryGetValue(key, out var value))
		{
			return value;
		}
		Type type = FindLoadedType(fullNameOrName);
		if (type != null && typeof(Component).IsAssignableFrom(type))
		{
			LoadedTypeCache[key] = type;
			return type;
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			try
			{
				type = assembly.GetTypes().FirstOrDefault((Type type2) => typeof(Component).IsAssignableFrom(type2) && (string.Equals(type2.FullName, fullNameOrName, StringComparison.Ordinal) || string.Equals(type2.Name, fullNameOrName, StringComparison.Ordinal)));
			}
			catch
			{
				type = null;
			}
			if (type != null)
			{
				LoadedTypeCache[key] = type;
				return type;
			}
		}
		return null;
	}


	private static Component[] FindBattlegroundsStateComponents()
	{
		Type type = FindLoadedComponentType("Card");
		if (type == null)
		{
			return Object.FindObjectsOfType<Component>();
		}
		List<Component> list = new List<Component>();
		list.AddRange((IEnumerable<Component>)(object)Object.FindObjectsOfType<Transform>());
		string[] array = new string[8] { "Card", "ChoiceCardMgr", "MulliganManager", "TurnStartManager", "TurnTimer", "EndTurnButton", "ManaCrystal", "BaconEndGameScreen" };
		foreach (string text in array)
		{
			Type type2 = (string.Equals(text, "Card", StringComparison.Ordinal) ? type : FindLoadedComponentType(text));
			if (!(type2 == null))
			{
				list.AddRange(Object.FindObjectsOfType(type2).OfType<Component>());
			}
		}
		return list.ToArray();
	}


	private static bool TryFindDirectGameplayStateComponents(object gameState, int maxCards, out Component[] components, out int entityMapCount, out string scanSource)
	{
		components = Array.Empty<Component>();
		entityMapCount = 0;
		scanSource = "GameState.GetEntityMap";
		List<Component> list = new List<Component>();
		HashSet<int> hashSet = new HashSet<int>();
		object instance = InvokeNoArg(gameState, "GetEntityMap");
		entityMapCount = ReadIntMember(instance, "get_Count", 0);
		bool flag = entityMapCount > 512;
		if (flag)
		{
			Type type = FindLoadedComponentType("Card");
			if (type != null)
			{
				scanSource = "Unity typed live Card (large entity map)";
				foreach (Component item in Object.FindObjectsOfType(type).OfType<Component>())
				{
					if (!((Object)(object)item == (Object)null) && hashSet.Add(((Object)item).GetInstanceID()))
					{
						list.Add(item);
						if (list.Count >= maxCards)
						{
							break;
						}
					}
				}
			}
			else
			{
				flag = false;
			}
		}
		if (!flag && InvokeNoArg(instance, "get_Values") is IEnumerable enumerable)
		{
			int num = 0;
			foreach (object item2 in enumerable)
			{
				if (item2 == null)
				{
					continue;
				}
				num++;
				string zone = InvokeNoArg(item2, "GetZone")?.ToString() ?? "";
				string cardId = InvokeNoArg(item2, "GetCardId")?.ToString() ?? "";
				if (!IsDirectGameplayEntity(item2, zone, cardId))
				{
					continue;
				}
				object obj = InvokeNoArg(item2, "GetCard");
				Component val = (Component)((obj is Component) ? obj : null);
				if (val != null && !((Object)(object)val == (Object)null) && hashSet.Add(((Object)val).GetInstanceID()))
				{
					list.Add(val);
					if (list.Count >= maxCards)
					{
						break;
					}
				}
			}
			if (entityMapCount <= 0)
			{
				entityMapCount = num;
			}
		}
		string[] array = new string[5] { "ChoiceCardMgr", "MulliganManager", "TurnStartManager", "TurnTimer", "EndTurnButton" };
		for (int i = 0; i < array.Length; i++)
		{
			object obj2 = InvokeStaticNoArg(array[i], "Get");
			Component val2 = (Component)((obj2 is Component) ? obj2 : null);
			if (val2 != null && (Object)(object)val2 != (Object)null && hashSet.Add(((Object)val2).GetInstanceID()))
			{
				list.Add(val2);
			}
		}
		components = list.ToArray();
		return true;
	}


	private static bool IsDirectGameplayEntity(object entity, string zone, string cardId)
	{
		if (ContainsAny(zone, "HAND", "PLAY", "CHOICE", "SETASIDE"))
		{
			return true;
		}
		if (TryInvokeBool(entity, "IsCardButton") || TryInvokeBool(entity, "IsMoveMinionHoverTarget"))
		{
			return true;
		}
		return ContainsAny(cardId, "TB_BaconShop", "BaconShop", "BaconBuy", "BaconSell", "BaconRefresh", "BaconReroll", "BaconLock", "BaconTechUp", "DragSell");
	}


	private static Component[] FilterCurrentBattlegroundsStateComponents(IEnumerable<Component> components, int friendlyPlayerId)
	{
		List<Component> list = new List<Component>();
		List<(Component, string, int, int, string, bool)> list2 = new List<(Component, string, int, int, string, bool)>();
		foreach (Component component in components)
		{
			if ((Object)(object)component == (Object)null)
			{
				continue;
			}
			if (!string.Equals(((object)component).GetType().Name, "Card", StringComparison.Ordinal))
			{
				list.Add(component);
				continue;
			}
			object obj = InvokeNoArg(component, "GetEntity");
			if (obj == null || !TryGetGameObject(component, out var gameObject))
			{
				list.Add(component);
			}
			else
			{
				list2.Add((component, InvokeNoArg(obj, "GetZone")?.ToString() ?? "", ReadIntMember(obj, "GetZonePosition", 0), ReadIntMember(obj, "GetControllerId", 0), InvokeNoArg(obj, "GetCardId")?.ToString() ?? "", gameObject.activeInHierarchy));
			}
		}
		int shopPlayerId = list2.Where(((Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active) card) => card.CardId.StartsWith("TB_BaconShopBob", StringComparison.OrdinalIgnoreCase)).Select(((Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active) card) => card.PlayerId).FirstOrDefault((int playerId) => playerId > 0 && playerId != friendlyPlayerId);
		if (shopPlayerId <= 0)
		{
			shopPlayerId = list2.Where(((Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active) card) => card.Active && card.PlayerId > 0 && card.PlayerId != friendlyPlayerId && string.Equals(card.Zone, "PLAY", StringComparison.OrdinalIgnoreCase) && card.ZonePosition > 0).GroupBy(((Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active) card) => card.PlayerId).OrderByDescending((IGrouping<int, (Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active)> group) => group.Count())
				.Select((IGrouping<int, (Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active)> group) => group.Key)
				.FirstOrDefault();
		}
		Component[] second = list2.Where(((Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active) card) => (card.Active && IsBattlegroundsControlCardId(card.CardId)) || (card.Active && card.CardId.StartsWith("TB_BaconShopBob", StringComparison.OrdinalIgnoreCase)) || (string.Equals(card.Zone, "HAND", StringComparison.OrdinalIgnoreCase) && card.ZonePosition > 0 && card.PlayerId == friendlyPlayerId) || (string.Equals(card.Zone, "PLAY", StringComparison.OrdinalIgnoreCase) && card.ZonePosition > 0 && (card.PlayerId == friendlyPlayerId || (card.Active && card.PlayerId == shopPlayerId) || (shopPlayerId <= 0 && card.Active)))).Select(((Component Component, string Zone, int ZonePosition, int PlayerId, string CardId, bool Active) card) => card.Component).ToArray();
		return list.Concat(second).ToArray();
	}


	private static bool IsBattlegroundsControlCardId(string cardId)
	{
		if (!string.Equals(cardId, "TB_BaconShop_8p_Reroll_Button", StringComparison.OrdinalIgnoreCase) && !string.Equals(cardId, "TB_BaconShopLockAll_Button", StringComparison.OrdinalIgnoreCase) && !cardId.StartsWith("TB_BaconShopTechUp", StringComparison.OrdinalIgnoreCase) && !string.Equals(cardId, "TB_BaconShop_DragSell", StringComparison.OrdinalIgnoreCase) && !string.Equals(cardId, "TB_BaconShop_DragBuy", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(cardId, "TB_BaconShop_DragBuy_Spell", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}


	private static Component[] LimitDirectCardComponents(IEnumerable<Component> components, int maxCards)
	{
		int cardLimit = Math.Max(0, maxCards);
		int cardCount = 0;
		return components.Where((Component component) => (Object)(object)component == (Object)null || !string.Equals(((object)component).GetType().Name, "Card", StringComparison.Ordinal) || cardCount++ < cardLimit).ToArray();
	}


	private static object InvokeNoArg(object instance, string methodName)
	{
		if (instance == null)
		{
			return null;
		}
		return GetCachedZeroArgMethod(instance.GetType(), methodName)?.Invoke(instance, null);
	}


	private static string TryInvokeObjectArgMethod(object instance, string methodName, object argument)
	{
		if (instance == null || argument == null)
		{
			return null;
		}
		try
		{
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument);
			});
			if (methodInfo == null)
			{
				return null;
			}
			methodInfo.Invoke(instance, new object[1] { argument });
			return instance.GetType().Name + "." + methodInfo.Name + "(" + methodInfo.GetParameters()[0].ParameterType.Name + ")";
		}
		catch
		{
			return null;
		}
	}


	private static string TryInvokeZeroArgComponentMethod(Component component, string methodName)
	{
		if ((Object)(object)component == (Object)null)
		{
			return null;
		}
		try
		{
			MethodInfo methodInfo = ((object)component).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, methodName, StringComparison.Ordinal) && item.GetParameters().Length == 0);
			if (methodInfo == null)
			{
				return null;
			}
			methodInfo.Invoke(component, null);
			return ((object)component).GetType().Name + "." + methodInfo.Name + "()";
		}
		catch
		{
			return null;
		}
	}


	private static bool InvokeIntArg(object instance, string methodName, int value)
	{
		if (instance == null)
		{
			return false;
		}
		MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && CanPassInt(parameters[0].ParameterType);
		});
		if (methodInfo == null)
		{
			return false;
		}
		Type parameterType = methodInfo.GetParameters()[0].ParameterType;
		methodInfo.Invoke(instance, new object[1] { ConvertIntArgument(value, parameterType) });
		return true;
	}


	private static object InvokeIntArgObject(object instance, string methodName, int value)
	{
		if (instance == null)
		{
			return null;
		}
		try
		{
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && CanPassInt(parameters[0].ParameterType);
			});
			if (methodInfo == null)
			{
				return null;
			}
			Type parameterType = methodInfo.GetParameters()[0].ParameterType;
			return methodInfo.Invoke(instance, new object[1] { ConvertIntArgument(value, parameterType) });
		}
		catch
		{
			return null;
		}
	}


	private static bool TryReadBoolArrayElement(object instance, string fieldName, int index, out bool value)
	{
		value = false;
		try
		{
			if (!((instance?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(instance) is Array array) || index < 0 || index >= array.Length)
			{
				return false;
			}
			object value2 = array.GetValue(index);
			bool flag = default;
			int num;
			if (value2 is bool)
			{
				flag = (bool)value2;
				num = 1;
			}
			else
			{
				num = 0;
			}
			value = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool TrySetBoolArrayElement(object instance, string fieldName, int index, bool value)
	{
		try
		{
			if (!((instance?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(instance) is Array array) || index < 0 || index >= array.Length)
			{
				return false;
			}
			array.SetValue(value, index);
			return true;
		}
		catch
		{
			return false;
		}
	}


	private static bool TryInvokeIntArgBool(object instance, string methodName, int value)
	{
		if (instance == null)
		{
			return false;
		}
		MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
		{
			if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
			{
				return false;
			}
			ParameterInfo[] parameters = item.GetParameters();
			return parameters.Length == 1 && item.ReturnType == typeof(bool) && CanPassInt(parameters[0].ParameterType);
		});
		if (methodInfo == null)
		{
			return false;
		}
		Type parameterType = methodInfo.GetParameters()[0].ParameterType;
		object obj = methodInfo.Invoke(instance, new object[1] { ConvertIntArgument(value, parameterType) });
		bool flag = default;
		int num;
		if (obj is bool)
		{
			flag = (bool)obj;
			num = 1;
		}
		else
		{
			num = 0;
		}
		return (byte)((uint)num & (flag ? 1u : 0u)) != 0;
	}


	private static bool? TryInvokeEntityNullableBoolArgsBool(object instance, string methodName, object entity, bool? firstBoolArg, bool? secondBoolArg)
	{
		if (instance == null || entity == null)
		{
			return null;
		}
		try
		{
			MethodInfo methodInfo = instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal) || item.ReturnType != typeof(bool))
				{
					return false;
				}
				ParameterInfo[] parameters2 = item.GetParameters();
				return parameters2.Length == 3 && parameters2[0].ParameterType.IsInstanceOfType(entity) && CanPassNullableBool(parameters2[1].ParameterType) && CanPassNullableBool(parameters2[2].ParameterType);
			});
			if (methodInfo == null)
			{
				return null;
			}
			ParameterInfo[] parameters = methodInfo.GetParameters();
			return (methodInfo.Invoke(instance, new object[3]
			{
				entity,
				ConvertNullableBoolArgument(firstBoolArg, parameters[1].ParameterType),
				ConvertNullableBoolArgument(secondBoolArg, parameters[2].ParameterType)
			}) is bool value) ? new bool?(value) : ((bool?)null);
		}
		catch
		{
			return null;
		}
	}


	private static object InvokeEntityArg(object instance, string methodName, object entity)
	{
		if (instance == null || entity == null)
		{
			return null;
		}
		try
		{
			return instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(entity);
			})?.Invoke(instance, new object[1] { entity });
		}
		catch
		{
			return null;
		}
	}


	private static bool CanPassInt(Type type)
	{
		if (!(type == typeof(int)) && !(type == typeof(short)) && !(type == typeof(long)) && !(type == typeof(uint)) && !(type == typeof(ushort)) && !(type == typeof(ulong)))
		{
			return type.IsEnum;
		}
		return true;
	}


	private static bool CanPassNullableBool(Type type)
	{
		if (!(type == typeof(bool)))
		{
			return type == typeof(bool?);
		}
		return true;
	}


	private static object ConvertNullableBoolArgument(bool? value, Type type)
	{
		if (type == typeof(bool?))
		{
			if (!value.HasValue)
			{
				return null;
			}
			return value.Value;
		}
		return value == true;
	}


	private static object ConvertIntArgument(int value, Type type)
	{
		if (type.IsEnum)
		{
			return Enum.ToObject(type, value);
		}
		return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
	}


	private static int ReadIntMember(object instance, string methodName, int defaultValue)
	{
		if (instance == null)
		{
			return defaultValue;
		}
		try
		{
			object obj = InvokeNoArg(instance, methodName);
			return (obj == null) ? defaultValue : Convert.ToInt32(obj, CultureInfo.InvariantCulture);
		}
		catch
		{
			return defaultValue;
		}
	}


	private static MethodInfo GetCachedZeroArgMethod(Type type, string methodName)
	{
		string key = BuildMemberCacheKey(type, methodName);
		return ZeroArgMethodCache.GetOrAdd(key, (string _) => new CachedMethod(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) => string.Equals(item.Name, methodName, StringComparison.Ordinal) && item.GetParameters().Length == 0))).Method;
	}


	private static object InvokeFirstNoArg(object instance, params string[] methodNames)
	{
		foreach (string methodName in methodNames)
		{
			object obj = InvokeNoArg(instance, methodName);
			if (obj != null)
			{
				return obj;
			}
		}
		return null;
	}


	private static object SafeEntitySummary(object entity)
	{
		try
		{
			Type type = entity.GetType();
			object gameState = GetGameState();
			object obj = InvokeNoArg(entity, "GetZone");
			string cardText = ((!ContainsAny(obj?.ToString() ?? "", "HAND", "CHOICE", "SETASIDE")) ? null : InvokeFirstNoArg(entity, "GetCardTextInHand")?.ToString());
			bool? hasResponse = TryInvokeEntityNullableBoolArgsBool(gameState, "HasResponse", entity, false, null);
			bool? isValidOption = TryInvokeEntityNullableBoolArgsBool(gameState, "IsValidOption", entity, false, null);
			string playError = InvokeEntityArg(gameState, "GetErrorType", entity)?.ToString();
			string playErrorParam = InvokeEntityArg(gameState, "GetErrorParam", entity)?.ToString();
			return new
			{
				type = type.FullName,
				id = InvokeNoArg(entity, "GetEntityId"),
				cardId = InvokeNoArg(entity, "GetCardId"),
				name = InvokeNoArg(entity, "GetName"),
				debugName = InvokeNoArg(entity, "GetDebugName"),
				zone = obj,
				zonePosition = InvokeNoArg(entity, "GetZonePosition"),
				cardText = cardText,
				realTimeCost = InvokeNoArg(entity, "GetRealTimeCost"),
				durability = InvokeFirstNoArg(entity, "GetDurability", "GetRealTimeDurability"),
				locationCooldown = InvokeFirstNoArg(entity, "GetLocationCooldown", "GetCooldown"),
				attack = InvokeFirstNoArg(entity, "GetATK", "GetAtk", "GetAttack"),
				health = InvokeFirstNoArg(entity, "GetHealth"),
				frozen = ReadEntityTagBool(entity, "FROZEN"),
				damage = InvokeFirstNoArg(entity, "GetDamage", "GetDamageAmount"),
				armor = InvokeFirstNoArg(entity, "GetArmor", "GetArmorAmount"),
				techLevel = InvokeFirstNoArg(entity, "GetTechLevel", "GetTier", "GetTavernTier"),
				isSpell = InvokeNoArg(entity, "IsSpell"),
				isBaconSpell = InvokeNoArg(entity, "IsBaconSpell"),
				isMinion = InvokeNoArg(entity, "IsMinion"),
				isHero = InvokeNoArg(entity, "IsHero"),
				isWeapon = InvokeNoArg(entity, "IsWeapon"),
				isHeroPower = InvokeNoArg(entity, "IsHeroPower"),
				taunt = ReadEntityTagBool(entity, "TAUNT"),
				divineShield = ReadEntityTagBool(entity, "DIVINE_SHIELD"),
				rush = ReadEntityTagBool(entity, "RUSH"),
				charge = ReadEntityTagBool(entity, "CHARGE"),
				stealth = ReadEntityTagBool(entity, "STEALTH"),
				cantBeTargetedBySpells = ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_SPELLS"),
				cantBeTargetedByHeroPowers = ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_HERO_POWERS"),
				cantBeTargetedByAbilities = ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_ABILITIES"),
				cantBeTargetedByOpponentAbilities = ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_OPPONENT_ABILITIES"),
				cantBeTargetedByOpponents = ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_OPPONENTS"),
				immune = ReadEntityTagBool(entity, "IMMUNE"),
				hasResponse = hasResponse,
				isValidOption = isValidOption,
				playError = playError,
				playErrorParam = playErrorParam
			};
		}
		catch
		{
			return entity.ToString();
		}
	}


	private static object DescribePlayErrorInfo(object playErrorInfo)
	{
		if (playErrorInfo == null)
		{
			return null;
		}
		try
		{
			return new
			{
				valid = TryInvokeBool(playErrorInfo, "IsValid"),
				playError = InvokeNoArg(playErrorInfo, "get_PlayError")?.ToString(),
				playErrorParam = InvokeNoArg(playErrorInfo, "get_PlayErrorParam")?.ToString()
			};
		}
		catch
		{
			return playErrorInfo.ToString();
		}
	}


	private static object[] ReadBattlegroundsPlayerRatings(object gameState)
	{
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		if (!(InvokeNoArg(InvokeNoArg(gameState, "GetEntityMap"), "get_Values") is IEnumerable enumerable))
		{
			return Array.Empty<object>();
		}
		foreach (object item in enumerable)
		{
			if (item == null)
			{
				continue;
			}
			string a = InvokeNoArg(item, "GetCardType")?.ToString() ?? "";
			if (ReadEntityTagInt(item, "CARDTYPE") == 2 || string.Equals(a, "PLAYER", StringComparison.OrdinalIgnoreCase))
			{
				int num = ReadEntityTagInt(item, "PLAYER_ID") ?? ReadIntMember(item, "GetPlayerId", 0);
				int num2 = ReadEntityTagInt(item, "BACON_RATING_HIDDEN") ?? ReadEntityTagInt(item, "BACON_RATING") ?? ReadIntMember(item, "GetBattlegroundsRating", 0);
				if (num2 <= 0)
				{
					num2 = ReadIntMember(item, "GetBaconRating", 0);
				}
				if (num >= 1 && num <= 7 && num2 > 0)
				{
					dictionary[num] = num2;
				}
			}
		}
		return ((IEnumerable<KeyValuePair<int, int>>)dictionary.OrderBy((KeyValuePair<int, int> pair) => pair.Key)).Select((Func<KeyValuePair<int, int>, object>)((KeyValuePair<int, int> pair) => new
		{
			playerId = pair.Key,
			rating = pair.Value
		})).ToArray();
	}


	private static object[] ReadBattlegroundsPlayerRaceCounts()
	{
		Type type = FindLoadedComponentType("PlayerLeaderboardPlayerOverlay");
		if (type == null)
		{
			return Array.Empty<object>();
		}
		Dictionary<int, Dictionary<int, int>> dictionary = new Dictionary<int, Dictionary<int, int>>();
		foreach (Component item in Resources.FindObjectsOfTypeAll(type).OfType<Component>().Where((Component component) =>
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_000b: Unknown result type (might be due to invalid IL or missing references)
			Scene scene = component.gameObject.scene;
			return scene.IsValid();
		}))
		{
			object obj = ReadMember(item, "HeroEntity");
			int num = ReadEntityTagInt(obj, "PLAYER_ID") ?? ReadIntMember(obj, "GetPlayerId", 0);
			bool flag = ((num < 1 || num > 8) ? true : false);
			if (flag || !(ReadMember(item, "m_raceCounts") is IEnumerable enumerable))
			{
				continue;
			}
			Dictionary<int, int> dictionary2 = new Dictionary<int, int>();
			foreach (object item2 in enumerable)
			{
				try
				{
					int num2 = Convert.ToInt32(ReadMember(item2, "Key"), CultureInfo.InvariantCulture);
					int num3 = Convert.ToInt32(ReadMember(item2, "Value"), CultureInfo.InvariantCulture);
					if (num2 > 0 && num3 > 0)
					{
						dictionary2[num2] = num3;
					}
				}
				catch
				{
				}
			}
			dictionary[num] = dictionary2;
		}
		return ((IEnumerable<KeyValuePair<int, Dictionary<int, int>>>)dictionary.OrderBy((KeyValuePair<int, Dictionary<int, int>> pair) => pair.Key)).Select((Func<KeyValuePair<int, Dictionary<int, int>>, object>)((KeyValuePair<int, Dictionary<int, int>> pair) => new
		{
			playerId = pair.Key,
			races = (from race in pair.Value
				orderby race.Key
				select new
				{
					race = race.Key,
					count = race.Value
				}).ToArray()
		})).ToArray();
	}


	private static bool ReadEntityTagBool(object entity, string tagName)
	{
		int? num = ReadEntityTagInt(entity, tagName);
		if (num.HasValue)
		{
			return num.Value > 0;
		}
		return false;
	}


	private static int? ReadEntityTagInt(object entity, string tagName)
	{
		if (entity == null || string.IsNullOrWhiteSpace(tagName))
		{
			return null;
		}
		string[] array = new string[3] { "GetTag", "GetRawTag", "GetGameTag" };
		foreach (string methodName in array)
		{
			int? num = TryInvokeEntityTagMethod(entity, methodName, tagName);
			if (num.HasValue)
			{
				return num.Value;
			}
		}
		array = new string[2] { "HasTag", "HasGameTag" };
		foreach (string methodName2 in array)
		{
			int? num2 = TryInvokeEntityTagMethod(entity, methodName2, tagName);
			if (num2.HasValue)
			{
				return num2.Value;
			}
		}
		return null;
	}


	private static int? TryInvokeEntityTagMethod(object entity, string methodName, string tagName)
	{
		try
		{
			Type entityType = entity.GetType();
			string key = entityType.FullName + "|" + methodName + "|" + tagName.ToUpperInvariant();
			MethodInfo method = EntityTagMethodCache.GetOrAdd(key, (string _) => new CachedMethod(entityType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo item) =>
			{
				if (!string.Equals(item.Name, methodName, StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = item.GetParameters();
				return parameters.Length == 1 && CanPassTagArgument(parameters[0].ParameterType, tagName);
			}))).Method;
			if (method == null)
			{
				return null;
			}
			object obj = ConvertTagArgument(tagName, method.GetParameters()[0].ParameterType);
			object obj2 = method.Invoke(entity, new object[1] { obj });
			int? result;
			if (obj2 is bool value)
			{
				result = (value ? 1 : 0);
			}
			else if (obj2 is int value2)
			{
				result = value2;
			}
			else if (obj2 is short value3)
			{
				result = value3;
			}
			else if (obj2 is byte value4)
			{
				result = value4;
			}
			else if (obj2 is long num)
			{
				result = (int)num;
			}
			else
			{
				result = ((!(obj2 is Enum value5)) ? ((int?)null) : new int?(Convert.ToInt32(value5, CultureInfo.InvariantCulture)));
			}
			return result;
		}
		catch
		{
			return null;
		}
	}


	private static bool CanPassTagArgument(Type parameterType, string tagName)
	{
		int value;
		if (parameterType == typeof(int) || parameterType == typeof(short) || parameterType == typeof(byte) || parameterType == typeof(long))
		{
			return TryResolveGameTagValue(tagName, out value);
		}
		if (parameterType.IsEnum)
		{
			return Enum.GetNames(parameterType).Any((string name) => string.Equals(name, tagName, StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}


	private static object ConvertTagArgument(string tagName, Type parameterType)
	{
		if (parameterType.IsEnum)
		{
			return Enum.Parse(parameterType, tagName, ignoreCase: true);
		}
		if (!TryResolveGameTagValue(tagName, out var value))
		{
			value = 0;
		}
		return ConvertIntArgument(value, parameterType);
	}


	private static bool TryResolveGameTagValue(string tagName, out int value)
	{
		string key = tagName.ToUpperInvariant();
		return (value = GameTagValueCache.GetOrAdd(key, (string _) =>
		{
			if (!TryGetGameTagType(out var tagType))
			{
				return -1;
			}
			string[] names = Enum.GetNames(tagType);
			foreach (string text in names)
			{
				if (string.Equals(text, tagName, StringComparison.OrdinalIgnoreCase))
				{
					return Convert.ToInt32(Enum.Parse(tagType, text), CultureInfo.InvariantCulture);
				}
			}
			return -1;
		})) >= 0;
	}


	private static bool TryGetGameTagType(out Type tagType)
	{
		if (_gameTagTypeResolved)
		{
			tagType = _gameTagType;
			return tagType != null;
		}
		lock (GameTagTypeLock)
		{
			if (!_gameTagTypeResolved)
			{
				string[] array = new string[3] { "GameTag", "TAG", "Hearthstone.GameTag" };
				for (int i = 0; i < array.Length; i++)
				{
					Type type = FindLoadedType(array[i]);
					if (!(type == null) && type.IsEnum)
					{
						_gameTagType = type;
						break;
					}
				}
				_gameTagTypeResolved = true;
			}
		}
		tagType = _gameTagType;
		return tagType != null;
	}


	private static object DescribeLiteCard(GameObject gameObject, Component card)
	{
		object obj = (((Object)(object)card == (Object)null) ? null : InvokeNoArg(card, "GetEntity"));
		return new
		{
			instanceId = ((Object)gameObject).GetInstanceID(),
			name = ((Object)gameObject).name,
			path = GetPath(gameObject),
			activeInHierarchy = gameObject.activeInHierarchy,
			entity = ((obj == null) ? null : SafeEntitySummary(obj))
		};
	}


	private static object DescribeLiteCardFast(GameObject gameObject, Component card, object gameState, int friendlyPlayerId, bool includeConstructedState, bool nativeTradeTags = false)
	{
		object entity = (((Object)(object)card == (Object)null) ? null : InvokeNoArg(card, "GetEntity"));
		if (entity == null)
		{
			return DescribeLiteCard(gameObject, card);
		}
		int num = ReadIntMember(entity, "GetEntityId", 0);
		string text = InvokeNoArg(entity, "GetCardId")?.ToString() ?? "";
		string text2 = InvokeNoArg(entity, "GetName")?.ToString() ?? ((Object)gameObject).name ?? "";
		object obj = InvokeNoArg(entity, "GetZone");
		string text3 = obj?.ToString() ?? "";
		int num2 = ReadIntMember(entity, "GetZonePosition", 0);
		int num3 = ReadIntMember(entity, "GetControllerId", 0);
		int num4;
		bool? flag;
		if (gameState != null && (friendlyPlayerId <= 0 || num3 == friendlyPlayerId))
		{
			num4 = (TryInvokeBool(gameState, "IsFriendlySidePlayerTurn") ? 1 : 0);
			if (num4 != 0)
			{
				flag = TryInvokeEntityNullableBoolArgsBool(gameState, "HasResponse", entity, false, null);
				goto IL_0132;
			}
		}
		else
		{
			num4 = 0;
		}
		flag = false;
		goto IL_0132;
		IL_0132:
		bool? hasResponse = flag;
		bool? flag2 = ((num4 != 0) ? TryInvokeEntityNullableBoolArgsBool(gameState, "IsValidOption", entity, false, null) : new bool?(false));
		bool flag3 = num4 != 0 && (hasResponse == true || flag2 == false);
		bool flag4 = TryInvokeBool(entity, "IsMinion");
		string cardText;
		if (ContainsAny(text3, "HAND", "CHOICE", "SETASIDE"))
		{
			cardText = InvokeNoArg(entity, "GetCardTextInHand")?.ToString();
		}
		else
		{
			cardText = ((!((!includeConstructedState && num3 == friendlyPlayerId && string.Equals(text3, "PLAY", StringComparison.OrdinalIgnoreCase) && num2 > 0) & flag4) || string.IsNullOrWhiteSpace(text)) ? null : BattlegroundsCardTextCache.GetOrAdd(text, (string _) => InvokeNoArg(entity, "GetCardTextInHand")?.ToString() ?? ""));
		}
		bool? flag5 = (includeConstructedState ? (InvokeNoArg(entity, "CanBeTargetedBySpells") as bool?) : ((bool?)null));
		bool? flag6 = (includeConstructedState ? (InvokeNoArg(entity, "CanBeTargetedByHeroPowers") as bool?) : ((bool?)null));
		string text4 = (string.IsNullOrWhiteSpace(text) ? "_" : text);
		string name = $"{text2} [id={num} cardId={text4} zone={text3} zonePos={num2} player={num3}]";
		int instanceID = ((Object)gameObject).GetInstanceID();
		string path = GetPath(gameObject);
		bool activeInHierarchy = gameObject.activeInHierarchy;
		string? fullName = entity.GetType().FullName;
		int realTimeCost = ReadIntMember(entity, "GetRealTimeCost", 0);
		int durability = (includeConstructedState ? ReadIntMember(entity, "GetDurability", ReadEntityTagInt(entity, "DURABILITY").GetValueOrDefault()) : 0);
		int locationCooldown = (includeConstructedState ? ReadIntMember(entity, "GetLocationCooldown", 0) : 0);
		int attack = ReadIntMember(entity, "GetATK", 0);
		int health = ReadIntMember(entity, "GetHealth", 0);
		bool frozen = ReadEntityTagBool(entity, "FROZEN");
		int damage = (includeConstructedState ? ReadIntMember(entity, "GetDamage", 0) : 0);
		int armor = (includeConstructedState ? ReadIntMember(entity, "GetArmor", 0) : 0);
		int techLevel = ReadIntMember(entity, "GetTechLevel", 0);
		bool isSpell = TryInvokeBool(entity, "IsSpell");
		bool isBaconSpell = TryInvokeBool(entity, "IsBaconSpell");
		bool isMinion = flag4;
		bool isHero = includeConstructedState && TryInvokeBool(entity, "IsHero");
		bool isWeapon = includeConstructedState && TryInvokeBool(entity, "IsWeapon");
		bool isHeroPower = includeConstructedState && TryInvokeBool(entity, "IsHeroPower");
		bool? tradeable;
		if (nativeTradeTags && num3 == friendlyPlayerId && string.Equals(text3, "HAND", StringComparison.OrdinalIgnoreCase))
		{
			int? num5 = ReadEntityTagInt(entity, "TRADEABLE");
			if (num5.HasValue)
			{
				int valueOrDefault = num5.GetValueOrDefault();
				tradeable = valueOrDefault > 0;
				goto IL_0491;
			}
		}
		tradeable = null;
		goto IL_0491;
		IL_0491:
		return new
		{
			instanceId = instanceID,
			name = name,
			path = path,
			activeInHierarchy = activeInHierarchy,
			entity = new
			{
				type = fullName,
				id = num,
				cardId = text,
				name = text2,
				debugName = (string)null,
				zone = obj,
				zonePosition = num2,
				playerId = num3,
				cardText = cardText,
				realTimeCost = realTimeCost,
				durability = durability,
				locationCooldown = locationCooldown,
				attack = attack,
				health = health,
				frozen = frozen,
				damage = damage,
				armor = armor,
				techLevel = techLevel,
				isSpell = isSpell,
				isBaconSpell = isBaconSpell,
				isMinion = isMinion,
				isHero = isHero,
				isWeapon = isWeapon,
				isHeroPower = isHeroPower,
				tradeable = tradeable,
				taunt = (includeConstructedState && TryInvokeBool(entity, "HasTaunt")),
				divineShield = (includeConstructedState && TryInvokeBool(entity, "HasDivineShield")),
				rush = (includeConstructedState && TryInvokeBool(entity, "HasRush")),
				charge = (includeConstructedState && TryInvokeBool(entity, "HasCharge")),
				stealth = (includeConstructedState && TryInvokeBool(entity, "HasStealth")),
				cantBeTargetedBySpells = (flag5.HasValue && !flag5.Value),
				cantBeTargetedByHeroPowers = (flag6.HasValue && !flag6.Value),
				cantBeTargetedByAbilities = (includeConstructedState && ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_ABILITIES")),
				cantBeTargetedByOpponentAbilities = (includeConstructedState && ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_OPPONENT_ABILITIES")),
				cantBeTargetedByOpponents = (includeConstructedState && ReadEntityTagBool(entity, "CANT_BE_TARGETED_BY_OPPONENTS")),
				immune = (includeConstructedState && TryInvokeBool(entity, "IsImmune")),
				hasResponse = hasResponse,
				isValidOption = flag2,
				playError = ((!flag3) ? null : InvokeEntityArg(gameState, "GetErrorType", entity)?.ToString()),
				playErrorParam = ((!flag3) ? null : InvokeEntityArg(gameState, "GetErrorParam", entity)?.ToString())
			}
		};
	}


	private static object TryDescribeLiteChoiceCardFast(GameObject gameObject, object gameState, int friendlyPlayerId, bool includeConstructedState)
	{
		try
		{
			Component val = FindCardComponent(gameObject);
			return ((Object)(object)val == (Object)null) ? null : DescribeLiteCardFast(gameObject, val, gameState, friendlyPlayerId, includeConstructedState);
		}
		catch
		{
			return null;
		}
	}


	private static bool IsFriendlyChoicesShown(Component manager)
	{
		if (!(bool.TryParse(ReadMemberAsString(manager, "m_friendlyChoicesShown"), out var result) & result))
		{
			return TryInvokeBool(manager, "IsFriendlyShown");
		}
		return true;
	}


	private static bool ContainsAny(string text, params string[] needles)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		foreach (string value in needles)
		{
			if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}


	private static bool IsBattlegroundsEndGamePlacementObject(GameObject gameObject)
	{
		if (!(((Object)gameObject).name ?? "").StartsWith("Placement_", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return GetPath(gameObject).IndexOf("/BaconTwoScoop", StringComparison.OrdinalIgnoreCase) >= 0;
	}


	private static int ReadBattlegroundsPlacement(object friendlySidePlayer, Component endGameScreen)
	{
		int num = ReadIntMember(endGameScreen, "get_Place", ReadMemberAsInt(endGameScreen, "m_place", 0));
		if (num >= 1 && num <= 8)
		{
			return num;
		}
		if ((Object)(object)endGameScreen != (Object)null && TryGetGameObject(endGameScreen, out var gameObject))
		{
			GameObject val = (from item in gameObject.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select ((Component)item).gameObject).FirstOrDefault((GameObject val2) => val2.activeInHierarchy && IsBattlegroundsEndGamePlacementObject(val2));
			Match match = (((Object)(object)val == (Object)null) ? Match.Empty : Regex.Match(((Object)val).name ?? "", "^Placement_(?<place>[1-8])(?:\\D|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
			if (match.Success && int.TryParse(match.Groups["place"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
			{
				return result;
			}
		}
		int valueOrDefault = ReadEntityTagInt(friendlySidePlayer, "PLAYER_LEADERBOARD_PLACE").GetValueOrDefault();
		if (valueOrDefault < 1 || valueOrDefault > 8)
		{
			return 0;
		}
		return valueOrDefault;
	}


	private static bool IsConstructedEndGameObject(GameObject gameObject)
	{
		return ContainsAny(((Object)gameObject).name + " " + GetPath(gameObject), "VictoryTwoScoop", "DefeatTwoScoop", "EndGameTwoScoop", "RankChangeTwoScoop");
	}


	private static string ResolveConstructedResult(int friendlyPlayState, int opposingPlayState, GameObject endGameScreen)
	{
		bool flag = friendlyPlayState == 4;
		if (!flag)
		{
			bool flag2 = ((opposingPlayState == 5 || opposingPlayState == 8) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return "Win";
		}
		flag = ((friendlyPlayState == 5 || friendlyPlayState == 8) ? true : false);
		if (flag || opposingPlayState == 4)
		{
			return "Loss";
		}
		if (friendlyPlayState == 6 || opposingPlayState == 6)
		{
			return "Draw";
		}
		string text = (((Object)(object)endGameScreen == (Object)null) ? "" : (((Object)endGameScreen).name + " " + GetPath(endGameScreen)));
		if (text.IndexOf("VictoryTwoScoop", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "Win";
		}
		if (text.IndexOf("DefeatTwoScoop", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "Loss";
		}
		return "Unknown";
	}


	private static bool IsLockOverlayName(string name)
	{
		return ContainsAny(name, "Lock", "Locked", "Disabled", "Cant", "CostLock");
	}


	private static void AppendActiveHandLockOverlayPaths(GameObject cardObject, Component card, int friendlyPlayerId, ICollection<string> lockOverlayPaths)
	{
		object obj = (((Object)(object)card == (Object)null) ? null : InvokeNoArg(card, "GetEntity"));
		if (obj == null || !string.Equals(InvokeNoArg(obj, "GetZone")?.ToString(), "HAND", StringComparison.OrdinalIgnoreCase) || (friendlyPlayerId > 0 && ReadIntMember(obj, "GetControllerId", 0) != friendlyPlayerId))
		{
			return;
		}
		Transform[] componentsInChildren = cardObject.GetComponentsInChildren<Transform>(true);
		foreach (Transform val in componentsInChildren)
		{
			if (!((Object)(object)val == (Object)null) && !((Object)(object)((Component)val).gameObject == (Object)(object)cardObject) && ((Component)val).gameObject.activeInHierarchy && ContainsAny(((Object)((Component)val).gameObject).name, "Card_Hand_LiterallyUnplayable", "Card_Hand_CostLock"))
			{
				lockOverlayPaths.Add(GetPath(((Component)val).gameObject));
			}
		}
	}


	private static bool IsBattlegroundsHeroCardId(string cardId)
	{
		if (string.IsNullOrWhiteSpace(cardId) || cardId.IndexOf("_Buddy", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return false;
		}
		if (!cardId.StartsWith("TB_BaconShop_HERO", StringComparison.OrdinalIgnoreCase) && cardId.IndexOf("_HERO_", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return cardId.StartsWith("HERO_", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}


	private static bool HasActiveDescendantNamed(GameObject root, string expectedName)
	{
		if ((Object)(object)root != (Object)null)
		{
			return (from item in root.GetComponentsInChildren<Transform>(true)
				where (Object)(object)item != (Object)null && (Object)(object)((Component)item).gameObject != (Object)null
				select item).Any((Transform item) => ((Component)item).gameObject.activeInHierarchy && string.Equals(((Object)((Component)item).gameObject).name, expectedName, StringComparison.Ordinal));
		}
		return false;
	}


	private static bool IsLikelyQuestNotificationPopupObject(string name, string path)
	{
		string text = name + " " + path;
		if (!ContainsAny(text, "QuestNotificationPopup"))
		{
			return false;
		}
		return ContainsAny(text, "QuestNotificationPopup Popup Bone/QuestNotificationPopup");
	}


	private static string TryReadEntityIdFromPath(string path)
	{
		int num = (path ?? "").IndexOf("[id=", StringComparison.Ordinal);
		if (num < 0)
		{
			return null;
		}
		num += "[id=".Length;
		int num2 = path.IndexOf(' ', num);
		if (num2 <= num)
		{
			return null;
		}
		return path.Substring(num, num2 - num);
	}


	private static int? TryReadAvailableResources(object gameState)
	{
		try
		{
			object obj = InvokeNoArg(InvokeNoArg(gameState, "GetFriendlySidePlayer") ?? InvokeNoArg(gameState, "GetLocalSidePlayer"), "GetNumAvailableResources");
			return (obj == null) ? ((int?)null) : new int?(Math.Max(0, Convert.ToInt32(obj, CultureInfo.InvariantCulture)));
		}
		catch
		{
			return null;
		}
	}


	private static string ReadMemberAsString(object instance, string name)
	{
		return ReadMember(instance, name)?.ToString();
	}


	private static object ReadMember(object instance, string name)
	{
		if (instance == null)
		{
			return null;
		}
		try
		{
			Type type = instance.GetType();
			PropertyInfo cachedInstanceProperty = GetCachedInstanceProperty(type, name);
			if (cachedInstanceProperty != null)
			{
				return cachedInstanceProperty.GetValue(instance, null);
			}
			return GetCachedInstanceField(type, name)?.GetValue(instance);
		}
		catch
		{
			return null;
		}
	}


	private static bool IsSameUnityObject(object value, Component expectedComponent, GameObject expectedGameObject)
	{
		try
		{
			if (value == null)
			{
				return false;
			}
			if ((Object)(object)expectedComponent != (Object)null && value == expectedComponent)
			{
				return true;
			}
			if ((Object)(object)expectedGameObject != (Object)null && value == expectedGameObject)
			{
				return true;
			}
			Component val = (Component)((value is Component) ? value : null);
			if (val != null)
			{
				if ((Object)(object)expectedComponent != (Object)null && ((Object)val).GetInstanceID() == ((Object)expectedComponent).GetInstanceID())
				{
					return true;
				}
				return (Object)(object)expectedGameObject != (Object)null && (Object)(object)val.gameObject != (Object)null && ((Object)val.gameObject).GetInstanceID() == ((Object)expectedGameObject).GetInstanceID();
			}
			GameObject val2 = (GameObject)((value is GameObject) ? value : null);
			return val2 != null && (Object)(object)expectedGameObject != (Object)null && ((Object)val2).GetInstanceID() == ((Object)expectedGameObject).GetInstanceID();
		}
		catch
		{
			return false;
		}
	}


	private static int ReadMemberAsInt(object instance, string name, int defaultValue)
	{
		if (!int.TryParse(ReadMemberAsString(instance, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return defaultValue;
		}
		return result;
	}


	private static long ReadMemberAsLong(object instance, string name, long defaultValue)
	{
		if (!long.TryParse(ReadMemberAsString(instance, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return defaultValue;
		}
		return result;
	}


	private static bool ReadMemberAsBool(object instance, string name, bool defaultValue)
	{
		if (!bool.TryParse(ReadMemberAsString(instance, name), out var result))
		{
			return defaultValue;
		}
		return result;
	}


	private static PropertyInfo GetCachedInstanceProperty(Type type, string name)
	{
		string key = BuildMemberCacheKey(type, name);
		return InstancePropertyCache.GetOrAdd(key, (string _) =>
		{
			PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return (property != null && property.GetIndexParameters().Length == 0) ? new CachedProperty(property) : CachedProperty.Missing;
		}).Property;
	}


	private static FieldInfo GetCachedInstanceField(Type type, string name)
	{
		string key = BuildMemberCacheKey(type, name);
		return InstanceFieldCache.GetOrAdd(key, (string _) => new CachedField(type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))).Field;
	}


	private static IEnumerable<GameObject> FindMatchingObjects(Selector selector)
	{
		if (selector.HasComponentSelector)
		{
			return EnumerateObjectsByComponent(selector);
		}
		return from gameObject in EnumerateObjects(!selector.HasExactIdentitySelector)
			where selector.Matches(gameObject)
			select gameObject;
	}


	private static GameObject ResolveObject(Selector selector)
	{
		if (selector.InstanceId.HasValue)
		{
			GameObject val = ResolveGameObjectByInstanceId(selector.InstanceId.Value);
			if ((Object)(object)val != (Object)null && selector.Matches(val))
			{
				return val;
			}
		}
		if (!string.IsNullOrWhiteSpace(selector.Path))
		{
			try
			{
				GameObject val2 = GameObject.Find(selector.Path);
				if ((Object)(object)val2 != (Object)null && selector.Matches(val2))
				{
					return val2;
				}
			}
			catch
			{
			}
		}
		return EnumerateObjects(!selector.HasExactIdentitySelector).FirstOrDefault((GameObject gameObject) => selector.Matches(gameObject));
	}


	private static GameObject ResolveGameObjectByInstanceId(int instanceId)
	{
		if (instanceId == 0 || FindObjectFromInstanceIdMethod == null)
		{
			return null;
		}
		try
		{
			object obj = FindObjectFromInstanceIdMethod.Invoke(null, new object[1] { instanceId });
			GameObject val = (GameObject)((obj is GameObject) ? obj : null);
			GameObject result;
			if (val == null)
			{
				Component val2 = (Component)((obj is Component) ? obj : null);
				result = ((val2 == null) ? null : val2.gameObject);
			}
			else
			{
				result = val;
			}
			return result;
		}
		catch
		{
			return null;
		}
	}


	private static IEnumerable<GameObject> EnumerateObjects(bool sort = true)
	{
		IEnumerable<GameObject> enumerable = from gameObject in Object.FindObjectsOfType<GameObject>()
			where IsBridgeVisibleObject(gameObject)
			select gameObject;
		if (!sort)
		{
			return enumerable;
		}
		return enumerable.OrderBy((GameObject gameObject) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			Scene scene = gameObject.scene;
			return scene.name;
		}).ThenBy(GetPath);
	}


	private static IEnumerable<GameObject> EnumerateObjectsByComponent(Selector selector)
	{
		HashSet<int> seen = new HashSet<int>();
		Type type = (string.Equals(selector.Component, "Card", StringComparison.OrdinalIgnoreCase) ? FindLoadedComponentType(selector.Component) : null);
		IEnumerable<Component> enumerable;
		if (!(type == null))
		{
			enumerable = Object.FindObjectsOfType(type).OfType<Component>();
		}
		else
		{
			IEnumerable<Component> enumerable2 = Object.FindObjectsOfType<Component>();
			enumerable = enumerable2;
		}
		IEnumerable<Component> enumerable3 = enumerable;
		foreach (Component item in enumerable3)
		{
			if (TryGetGameObject(item, out var gameObject) && IsBridgeVisibleObject(gameObject) && ComponentTypeMatches(item, selector.Component) && TryGetInstanceId(gameObject, out var instanceId) && seen.Add(instanceId) && selector.Matches(gameObject, componentAlreadyMatched: true))
			{
				yield return gameObject;
			}
		}
	}


	private static IEnumerable<GameObject> FindVisibleBoosterPackRewardObjects()
	{
		HashSet<int> seen = new HashSet<int>();
		Component[] array = Object.FindObjectsOfType<Component>();
		foreach (Component val in array)
		{
			if (!((Object)(object)val == (Object)null) && ComponentTypeMatches(val, "BoosterPackReward") && TryGetGameObject(val, out var gameObject) && IsBridgeVisibleObject(gameObject) && TryGetInstanceId(gameObject, out var instanceId) && seen.Add(instanceId) && IsShownBoosterPackRewardObject(gameObject, val))
			{
				yield return gameObject;
			}
		}
	}


	private static bool IsHiddenBoosterPackRewardObject(GameObject gameObject)
	{
		try
		{
			Transform val = (((Object)(object)gameObject == (Object)null) ? null : gameObject.transform);
			while ((Object)(object)val != (Object)null)
			{
				Component val2 = (from component in ((Component)val).gameObject.GetComponents<Component>()
					where (Object)(object)component != (Object)null
					select component).FirstOrDefault((Component component) => ComponentTypeMatches(component, "BoosterPackReward"));
				if ((Object)(object)val2 != (Object)null)
				{
					return !IsShownBoosterPackRewardObject(((Component)val).gameObject, val2);
				}
				val = val.parent;
			}
		}
		catch
		{
			return false;
		}
		return false;
	}


	private static bool IsShownBoosterPackRewardObject(GameObject gameObject, Component component)
	{
		if ((Object)(object)gameObject == (Object)null || (Object)(object)component == (Object)null || !gameObject.activeInHierarchy)
		{
			return false;
		}
		if (ReadMemberAsBool(component, "IsShown", defaultValue: false) || ReadMemberAsBool(component, "m_shown", defaultValue: false))
		{
			return true;
		}
		try
		{
			object obj = ReadMember(component, "m_root");
			GameObject val = (GameObject)((obj is GameObject) ? obj : null);
			return val != null && val.activeSelf && val.activeInHierarchy;
		}
		catch
		{
			return false;
		}
	}


	private static bool IsBridgeVisibleObject(GameObject gameObject)
	{
		try
		{
			return (Object)(object)gameObject != (Object)null && !((Object)gameObject).name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}


	private static bool ComponentTypeMatches(Component component, string componentName)
	{
		if (string.IsNullOrWhiteSpace(componentName))
		{
			return false;
		}
		Type type = ((object)component).GetType();
		while (type != null)
		{
			string? obj = type.FullName ?? "";
			string text = type.Name ?? "";
			if (obj.IndexOf(componentName, StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf(componentName, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			type = type.BaseType;
		}
		return false;
	}


	private static object Describe(GameObject gameObject, bool includeComponents)
	{
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0134: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_014f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0159: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0182: Unknown result type (might be due to invalid IL or missing references)
		//IL_018d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0192: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		RectTransform component = gameObject.GetComponent<RectTransform>();
		Renderer component2 = gameObject.GetComponent<Renderer>();
		Component val = (from val2 in gameObject.GetComponents<Component>()
			where (Object)(object)val2 != (Object)null
			select val2).FirstOrDefault((Component val2) => string.Equals(((object)val2).GetType().Name, "Card", StringComparison.Ordinal));
		object obj = (((Object)(object)val == (Object)null) ? null : InvokeNoArg(val, "GetEntity"));
		string[] components = (includeComponents ? (from val2 in gameObject.GetComponents<Component>()
			where (Object)(object)val2 != (Object)null
			select ((object)val2).GetType().FullName).Distinct().Take(24).ToArray() : Array.Empty<string>());
		int instanceID = ((Object)gameObject).GetInstanceID();
		string name = ((Object)gameObject).name;
		string path = GetPath(gameObject);
		string tag = SafeGetTag(gameObject);
		int layer = gameObject.layer;
		bool activeSelf = gameObject.activeSelf;
		bool activeInHierarchy = gameObject.activeInHierarchy;
		Scene scene = gameObject.scene;
		string name2 = scene.name;
		string objectText = GetObjectText(gameObject);
		object rect2;
		if (!((Object)(object)component == (Object)null))
		{
			float x = ((Transform)component).position.x;
			float y = ((Transform)component).position.y;
			float z = ((Transform)component).position.z;
			Rect rect = component.rect;
			float width = rect.width;
			rect = component.rect;
			rect2 = new { x, y, z, width, rect.height };
		}
		else
		{
			rect2 = null;
		}
		object bounds2;
		if (!((Object)(object)component2 == (Object)null))
		{
			Bounds bounds = component2.bounds;
			object center = Vec(bounds.center);
			bounds = component2.bounds;
			bounds2 = new
			{
				center = center,
				size = Vec(bounds.size)
			};
		}
		else
		{
			bounds2 = null;
		}
		return new
		{
			instanceId = instanceID,
			name = name,
			path = path,
			tag = tag,
			layer = layer,
			activeSelf = activeSelf,
			activeInHierarchy = activeInHierarchy,
			scene = name2,
			text = objectText,
			rect = rect2,
			bounds = bounds2,
			entity = ((obj == null) ? null : SafeEntitySummary(obj)),
			components = components
		};
	}


	private static object Vec(Vector3 value)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		return new { value.x, value.y, value.z };
	}


	private static string SafeGetTag(GameObject gameObject)
	{
		try
		{
			return gameObject.tag;
		}
		catch
		{
			return "";
		}
	}


	private static string GetPath(GameObject gameObject)
	{
		Stack<string> stack = new Stack<string>();
		Transform val = gameObject.transform;
		while ((Object)(object)val != (Object)null)
		{
			stack.Push(((Object)val).name);
			val = val.parent;
		}
		return "/" + string.Join("/", stack.ToArray());
	}


	private static string GetObjectText(GameObject gameObject)
	{
		return GetObjectText(from component in gameObject.GetComponents<Component>()
			where (Object)(object)component != (Object)null
			select component);
	}


	private static string ReadLikelyText(Component component)
	{
		TextMesh val = (TextMesh)(object)((component is TextMesh) ? component : null);
		if (val != null)
		{
			return val.text;
		}
		Text val2 = (Text)(object)((component is Text) ? component : null);
		if (val2 != null)
		{
			return val2.text;
		}
		Type type = ((object)component).GetType();
		if (!ContainsAny(type.Name ?? "", "Text", "Label", "TMP"))
		{
			return "";
		}
		string[] textMemberNames = TextMemberNames;
		foreach (string name in textMemberNames)
		{
			PropertyInfo cachedInstanceProperty = GetCachedInstanceProperty(type, name);
			if (cachedInstanceProperty != null)
			{
				try
				{
					if (cachedInstanceProperty.GetValue(component, null) is string text && !string.IsNullOrWhiteSpace(text))
					{
						return text;
					}
				}
				catch
				{
				}
			}
			FieldInfo cachedInstanceField = GetCachedInstanceField(type, name);
			if (!(cachedInstanceField != null))
			{
				continue;
			}
			try
			{
				if (cachedInstanceField.GetValue(component) is string text2 && !string.IsNullOrWhiteSpace(text2))
				{
					return text2;
				}
			}
			catch
			{
			}
		}
		return "";
	}


	private static string GetObjectText(IEnumerable<Component> components)
	{
		List<string> list = new List<string>();
		foreach (Component component in components)
		{
			Component val = component;
			TextMesh val2 = (TextMesh)(object)((val is TextMesh) ? val : null);
			if (val2 != null && !string.IsNullOrWhiteSpace(val2.text))
			{
				list.Add(val2.text);
			}
			Type type = ((object)component).GetType();
			string[] textMemberNames = TextMemberNames;
			foreach (string name in textMemberNames)
			{
				PropertyInfo property = GetCachedInstanceProperty(type, name);
				if (property != null)
				{
					TryAddText(list, () => property.GetValue(component, null));
				}
				FieldInfo field = GetCachedInstanceField(type, name);
				if (field != null)
				{
					TryAddText(list, () => field.GetValue(component));
				}
			}
		}
		return string.Join(" ", list.Where((string part) => !string.IsNullOrWhiteSpace(part)).Distinct().Take(8)
			.ToArray());
	}


	private static void TryAddText(List<string> parts, Func<object> getter)
	{
		try
		{
			if (getter() is string text && !string.IsNullOrWhiteSpace(text))
			{
				parts.Add(text);
			}
		}
		catch
		{
		}
	}


	private static string BuildMemberCacheKey(Type type, string memberName)
	{
		return (type.AssemblyQualifiedName ?? type.FullName ?? type.Name) + "::" + memberName;
	}


	private static bool IsPlayErrorInfoValid(object playErrorInfo)
	{
		if (playErrorInfo == null)
		{
			return true;
		}
		if (TryInvokeBool(playErrorInfo, "IsValid"))
		{
			return true;
		}
		return string.Equals(InvokeNoArg(playErrorInfo, "get_PlayError")?.ToString(), "NONE", StringComparison.OrdinalIgnoreCase);
	}


	private static int GetInt(string json, string name, int defaultValue)
	{
		Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)");
		if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return defaultValue;
		}
		return result;
	}


	private static int? GetNullableInt(string json, string name)
	{
		Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*(-?\\d+)");
		if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return result;
	}


	private static List<int> GetIntArray(string json, string name)
	{
		Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*\\[([^\\]]*)\\]");
		if (!match.Success)
		{
			return null;
		}
		return (from Match item in Regex.Matches(match.Groups[1].Value, "-?\\d+")
			select int.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0 into value
			where value > 0
			select value).Distinct().ToList();
	}


	private static bool GetBool(string json, string name, bool defaultValue)
	{
		Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*(true|false)", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return defaultValue;
		}
		return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
	}


	private static string GetString(string json, string name)
	{
		Match match = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
		if (!match.Success)
		{
			return null;
		}
		return UnescapeJson(match.Groups[1].Value);
	}


	private static string ExtractObject(string json, string name)
	{
		if (json == null)
		{
			json = "";
		}
		Match match = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\\{");
		if (!match.Success)
		{
			return null;
		}
		int num = match.Index + match.Length - 1;
		int num2 = 0;
		bool flag = false;
		bool flag2 = false;
		for (int i = num; i < json.Length; i++)
		{
			char c = json[i];
			if (flag2)
			{
				flag2 = false;
			}
			else if ((c == '\\') & flag)
			{
				flag2 = true;
			}
			else if (c == '"')
			{
				flag = !flag;
			}
			else
			{
				if (flag)
				{
					continue;
				}
				switch (c)
				{
				case '{':
					num2++;
					break;
				case '}':
					num2--;
					if (num2 == 0)
					{
						return json.Substring(num, i - num + 1);
					}
					break;
				}
			}
		}
		return null;
	}


	private static string UnescapeJson(string value)
	{
		return value.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\/", "/")
			.Replace("\\n", "\n")
			.Replace("\\r", "\r")
			.Replace("\\t", "\t");
	}

}}
