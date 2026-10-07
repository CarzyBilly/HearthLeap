using System.Text.Json;
using HsAuto.Core.Automation;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;
using HsAuto.Core.Strategy;

namespace HsAuto.Open;

public sealed class TraditionalSettings
{
    public bool AutoConcede { get; set; }
    public bool WinThenConcede { get; set; }
    public int WinsBeforeConcede { get; set; } = 1;
    public int ConcedesAfterWins { get; set; } = 1;
    public bool LowWinRateConcede { get; set; }
    public int WinRateThreshold { get; set; } = 20;
    public bool RopeProtection { get; set; }
    public bool RotateDecks { get; set; }
    public int RotateEvery { get; set; } = 1;
    public string RotationNames { get; set; } = "";
    public bool StopAtRank { get; set; }
    public string StopRank { get; set; } = "Diamond5";
    public bool ControlRank { get; set; }
    public string ControlRankTarget { get; set; } = "Diamond5";
    public bool StopAtLegend { get; set; }
    public int LegendThreshold { get; set; } = 30000;
    public bool RestartBox { get; set; }
    public int MissingRecommendationSeconds { get; set; } = 15;
    public int RestartCooldownSeconds { get; set; } = 120;

    public void Normalize()
    {
        WinsBeforeConcede = Math.Clamp(WinsBeforeConcede, 1, 999);
        ConcedesAfterWins = Math.Clamp(ConcedesAfterWins, 1, 999);
        WinRateThreshold = Math.Clamp(WinRateThreshold, 1, 100);
        RotateEvery = Math.Clamp(RotateEvery, 1, 999);
        LegendThreshold = Math.Clamp(LegendThreshold, 1, 1000000);
        MissingRecommendationSeconds = Math.Clamp(MissingRecommendationSeconds, 5, 120);
        RestartCooldownSeconds = Math.Clamp(RestartCooldownSeconds, 30, 3600);
        StopRank = AccountRunStopPolicy.NormalizeConstructedRankTarget(StopRank);
        ControlRankTarget = ConstructedRankControlPolicy.NormalizeTarget(ControlRankTarget);
        RotationNames ??= "";
    }
    public void Validate(string format)
    {
        if (WinsBeforeConcede is < 1 or > 999 || ConcedesAfterWins is < 1 or > 999 ||
            WinRateThreshold is < 1 or > 100 || RotateEvery is < 1 or > 999 ||
            LegendThreshold is < 1 or > 1000000 || MissingRecommendationSeconds is < 5 or > 120 ||
            RestartCooldownSeconds is < 30 or > 3600) throw new IOException("传统设置数值超出允许范围，请检查输入。");
        if (format == "Casual" && (StopAtRank || ControlRank || StopAtLegend)) throw new IOException("休闲模式没有排位段位，请关闭段位停止、控制分段和传说排名停止。");
    }
    public ConstructedSettings Runtime(Settings s, string deck) => new()
    {
        Format = ConstructedSettings.ParseFormat(s.Format), DeckName = deck,
        AutoQueue = s.AutoQueue, DryRun = s.ReadOnly,
        AutoClaimRewardTrack = false, AutoClaimAchievements = false,
        EnableHumanizedAutomation = false,
        EnableNeteaseBoxLowWinRateAutoConcede = LowWinRateConcede,
        NeteaseBoxLowWinRateConcedeThresholdPercent = WinRateThreshold,
        EnableNeteaseStallConcedeProtection = RopeProtection,
        EnableNeteaseBoxAutoRecovery = RestartBox && !s.ReadOnly,
        NeteaseBoxStallTimeoutSeconds = MissingRecommendationSeconds,
        NeteaseBoxRecoveryCooldownSeconds = RestartCooldownSeconds
    };
}

public sealed record DeckEntry(string Name, bool Standard, bool Wild, bool Playable);
public static class DeckCatalog
{
    // This command only reads CollectionManager / cached pages: never changes the scene or deck.
    public const string Command = "constructedVisibleDeckCatalog";
    public static IReadOnlyList<DeckEntry> Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("decks", out var decks) || decks.ValueKind != JsonValueKind.Array)
            throw new IOException("插件未返回有效的卡组清单。");
        return decks.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Object)
            .Select(d => new DeckEntry(Text(d, "name").Trim(), Bool(d, "isStandard"), Bool(d, "isWild"), ConstructedDeckRotationPolicy.IsPlayableCatalogEntry(d)))
            .Where(d => d.Name.Length > 0).DistinctBy(d => (d.Name, d.Standard, d.Wild)).ToArray();
    }
    public static IReadOnlyList<string> Names(IEnumerable<DeckEntry> entries, string format) => entries
        .Where(d => d.Playable && (format == "Casual" || format == "Standard" && d.Standard || format == "Wild" && (d.Wild || d.Standard)))
        .Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    internal static string Text(JsonElement d, string key) => d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    internal static bool Bool(JsonElement d, string key) => d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    internal static int Int(JsonElement d, string key) => d.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
}

public static class RankReader
{
    public static HearthstoneConstructedRankSnapshot Parse(JsonElement data, string format)
    {
        if (format == "Casual" || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("constructedRanks", out var ranks) ||
            ranks.ValueKind != JsonValueKind.Object || !ranks.TryGetProperty(format == "Wild" ? "wild" : "standard", out var rank) || rank.ValueKind != JsonValueKind.Object)
            return HearthstoneConstructedRankSnapshot.Unavailable(format, "等待段位数据");
        // GameAdapter serializes the public PascalCase properties of ConstructedRankReadResult.
        return new(DeckCatalog.Bool(rank, "Available"), format, DeckCatalog.Bool(rank, "IsNewPlayer"), DeckCatalog.Bool(rank, "IsLegend"),
            DeckCatalog.Int(rank, "LegendRank"), DeckCatalog.Int(rank, "StarLevel"), DeckCatalog.Int(rank, "LeagueId"),
            DeckCatalog.Text(rank, "RankName"), DeckCatalog.Text(rank, "MedalText"), DeckCatalog.Text(rank, "CheatName"),
            DeckCatalog.Text(rank, "DisplayName"), DeckCatalog.Text(rank, "Reason")) { Stars = DeckCatalog.Int(rank, "Stars") };
    }
    public static string StopReason(TraditionalSettings s, HearthstoneConstructedRankSnapshot r)
    {
        if (!r.Available || r.IsNewPlayer) return "";
        var target = AccountRunStopPolicy.GetConstructedRankTarget(s.StopRank);
        if (s.StopAtRank && (r.IsLegend || r.StarLevel >= target.MinimumStarLevel)) return "已达到停止段位：" + r.DisplayName;
        if (s.StopAtLegend && r.IsLegend && r.LegendRank > 0 && r.LegendRank <= s.LegendThreshold) return $"传说排名 {r.LegendRank} 已进入 {s.LegendThreshold} 名以内，停止匹配。";
        return "";
    }
}

public sealed class TraditionalSession
{
    readonly TraditionalSettings settings;
    readonly HashSet<string> recorded = new(StringComparer.Ordinal);
    public int Completed { get; private set; }
    public int WinsInCycle { get; private set; }
    public int RemainingConcedes { get; private set; }
    public int ConsecutiveRopeConcedes { get; private set; }
    bool patternConcede;
    bool ropeConcede;
    bool rankConcede;
    bool draining;
    public TraditionalSession(TraditionalSettings s) => settings = s;
    public void SetRank(HearthstoneConstructedRankSnapshot rank)
    {
        var decision = ConstructedRankControlPolicy.Evaluate(settings.ControlRank, settings.ControlRankTarget, rank, draining);
        rankConcede = decision.ShouldConcede; draining = decision.ShouldConcede;
    }
    public void BeginMatch() { patternConcede = RemainingConcedes > 0; ropeConcede = false; }
    public void RecordConcede(ConstructedAction action) => ropeConcede = action.Parameters.ContainsKey("hsauto.neteaseConstructedStallConcede");
    public bool RecordResult(string id, ConstructedGameResult result)
    {
        if (result == ConstructedGameResult.Unknown || !recorded.Add(id)) return false;
        Completed++;
        ConsecutiveRopeConcedes = ropeConcede ? ConsecutiveRopeConcedes + 1 : 0;
        if (patternConcede && result == ConstructedGameResult.Loss && RemainingConcedes > 0) RemainingConcedes--;
        else if (settings.WinThenConcede && result == ConstructedGameResult.Win && RemainingConcedes == 0)
        {
            WinsInCycle++;
            if (WinsInCycle >= settings.WinsBeforeConcede) { WinsInCycle = 0; RemainingConcedes = settings.ConcedesAfterWins; }
        }
        return true;
    }
    public ConstructedAction Concede(ConstructedGameState state, ConstructedWinRateSnapshot winRate, DateTimeOffset now)
    {
        if (state.Phase is not (ConstructedPhase.Mulligan or ConstructedPhase.LocalTurn)) return null;
        string reason = settings.AutoConcede ? "自动投降已开启" : patternConcede ? $"赢后投降：本轮还需投降 {RemainingConcedes} 场" : rankConcede ? "达到控制分段控星线" : "";
        if (reason.Length == 0 && state.Phase == ConstructedPhase.LocalTurn && state.ManaTotal >= 4 && settings.LowWinRateConcede && winRate != null &&
            SameGame(winRate.MatchId, state.MatchId) && winRate.TurnId == state.Turn && now >= winRate.CapturedAt && now - winRate.CapturedAt <= TimeSpan.FromSeconds(20) &&
            double.IsFinite(winRate.Percent) && winRate.Percent >= 0 && winRate.Percent < settings.WinRateThreshold)
            reason = $"盒子当前回合胜率 {winRate.Percent:0.0}% 低于 {settings.WinRateThreshold}%";
        return reason.Length == 0 ? null : new() { Type = ConstructedActionType.Concede, Reason = reason, Parameters = new Dictionary<string, string> { ["hsauto.automaticConcede"] = "True" } };
    }
    static bool SameGame(string a, string b)
    {
        static string Key(string s) => s.StartsWith("constructed-", StringComparison.OrdinalIgnoreCase) && s.LastIndexOf('-') > 0 ? s[..s.LastIndexOf('-')] : s;
        return Key(a) == Key(b);
    }
    public string Rotate(string current, IReadOnlyList<string> available)
    {
        if (!settings.RotateDecks || Completed == 0 || Completed % settings.RotateEvery != 0) return current;
        var specified = settings.RotationNames.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var pool = specified.Length == 0 ? available.ToArray() : specified;
        if (pool.Any(n => !available.Contains(n, StringComparer.OrdinalIgnoreCase))) throw new IOException("轮换列表包含当前模式未识别到的完整卡组，请检查名称或刷新卡组。");
        if (pool.Length < 2) throw new IOException("轮换卡组至少需要两副当前模式可用的完整卡组。");
        int index = Array.FindIndex(pool, n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        return pool[(index + 1) % pool.Length];
    }
}
