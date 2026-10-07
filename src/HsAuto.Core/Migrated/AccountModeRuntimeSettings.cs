// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Linq;

namespace HsAuto.Core.Configuration;
public sealed record AccountModeRuntimeSettings
{
    public bool ConstructedAutoConcede { get; init; }
    public bool ConstructedWinThenConcede { get; init; }
    public int ConstructedWinsBeforeConcede { get; init; } = 1;
    public int ConstructedConcedesAfterWins { get; init; } = 1;
    public bool EnableConstructedRankControl { get; init; }
    public string ConstructedRankControlTarget { get; init; } = "Diamond5";
    public bool EnableNeteaseBoxLowWinRateAutoConcede { get; init; }
    public bool EnableArenaAi2LowWinRateAutoConcede { get; init; }
    public int ArenaAi2LowWinRateConcedeThresholdPercent { get; init; } = 20;
    public int NeteaseBoxLowWinRateConcedeThresholdPercent { get; init; } = 20;
    public bool EnableConstructedDeckRotation { get; init; }
    public int ConstructedDeckRotationInterval { get; init; }
    public string ConstructedDeckRotationDeckNames { get; init; } = "";
    public bool EnableStopAtConstructedRank { get; init; }
    public string StopAtConstructedRank { get; init; } = "Diamond5";
    public bool EnableStopAtLegendRank { get; init; }
    public int StopAtLegendRank { get; init; } = 30000;
    public bool EnableNeteaseBoxConstructedAutoRecovery { get; init; }
    public bool EnableNeteaseConstructedStallConcedeProtection { get; init; }
    public bool? EnableHumanizedAutomation { get; init; }
    public bool EnableStrongAiHumanizedPacing { get; init; } = true;
    public bool HumanizedAutomationEnabled => EnableHumanizedAutomation ?? EnableStrongAiHumanizedPacing;
    public int NeteaseBoxConstructedRecoveryCooldownSeconds { get; init; } = 120;
    public int NeteaseBoxConstructedStallTimeoutSeconds { get; init; } = 15;
    public bool EnableBattlegroundsQuickCombat { get; init; }
    public bool EnableBattlegroundsTimeGear { get; init; }
    public decimal BattlegroundsTimeGearMultiplier { get; init; } = 2.0m;
    public bool EnableBattlegroundsScoreControl { get; init; }
    public int BattlegroundsScoreTarget { get; init; } = 6000;
    public bool EnableNeteaseBoxBattlegroundsAutoRecovery { get; init; }
    public int NeteaseBoxBattlegroundsRecoveryCooldownSeconds { get; init; } = 120;
    public int NeteaseBoxBattlegroundsStallTimeoutSeconds { get; init; } = 30;
    public bool EnableStopAfterArenaRounds { get; init; }
    public int StopAfterArenaRounds { get; init; } = 10;
    public bool DoNotPurchaseArenaTicketsWithGold { get; init; }
    public bool StopAfterUndergroundArenaUnlocked { get; init; }
    public bool SpendAllGoldOnArenaTickets { get; init; }

    public static AccountModeRuntimeSettings FromBotSettings(BotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings, "settings");
        return Normalize(new AccountModeRuntimeSettings { ConstructedAutoConcede = settings.ConstructedAutoConcede, ConstructedWinThenConcede = settings.ConstructedWinThenConcede, ConstructedWinsBeforeConcede = settings.ConstructedWinsBeforeConcede, ConstructedConcedesAfterWins = settings.ConstructedConcedesAfterWins, EnableConstructedRankControl = settings.EnableConstructedRankControl, ConstructedRankControlTarget = settings.ConstructedRankControlTarget, EnableNeteaseBoxLowWinRateAutoConcede = settings.EnableNeteaseBoxLowWinRateAutoConcede, EnableArenaAi2LowWinRateAutoConcede = settings.EnableArenaAi2LowWinRateAutoConcede, ArenaAi2LowWinRateConcedeThresholdPercent = settings.ArenaAi2LowWinRateConcedeThresholdPercent, NeteaseBoxLowWinRateConcedeThresholdPercent = settings.NeteaseBoxLowWinRateConcedeThresholdPercent, EnableConstructedDeckRotation = settings.EnableConstructedDeckRotation, ConstructedDeckRotationInterval = settings.ConstructedDeckRotationInterval, ConstructedDeckRotationDeckNames = settings.ConstructedDeckRotationDeckNames, EnableStopAtConstructedRank = settings.EnableStopAtConstructedRank, StopAtConstructedRank = settings.StopAtConstructedRank, EnableStopAtLegendRank = settings.EnableStopAtLegendRank, StopAtLegendRank = settings.StopAtLegendRank, EnableNeteaseBoxConstructedAutoRecovery = settings.EnableNeteaseBoxConstructedAutoRecovery, EnableNeteaseConstructedStallConcedeProtection = settings.EnableNeteaseConstructedStallConcedeProtection, EnableHumanizedAutomation = settings.HumanizedAutomationEnabled, EnableStrongAiHumanizedPacing = settings.EnableStrongAiHumanizedPacing, NeteaseBoxConstructedRecoveryCooldownSeconds = settings.NeteaseBoxConstructedRecoveryCooldownSeconds, NeteaseBoxConstructedStallTimeoutSeconds = settings.NeteaseBoxConstructedStallTimeoutSeconds, EnableBattlegroundsQuickCombat = settings.EnableBattlegroundsQuickCombat, EnableBattlegroundsTimeGear = settings.EnableBattlegroundsTimeGear, BattlegroundsTimeGearMultiplier = settings.BattlegroundsTimeGearMultiplier, EnableBattlegroundsScoreControl = settings.EnableBattlegroundsScoreControl, BattlegroundsScoreTarget = settings.BattlegroundsScoreTarget, EnableNeteaseBoxBattlegroundsAutoRecovery = settings.EnableNeteaseBoxBattlegroundsAutoRecovery, NeteaseBoxBattlegroundsRecoveryCooldownSeconds = settings.NeteaseBoxBattlegroundsRecoveryCooldownSeconds, NeteaseBoxBattlegroundsStallTimeoutSeconds = settings.NeteaseBoxBattlegroundsStallTimeoutSeconds, EnableStopAfterArenaRounds = settings.EnableStopAfterArenaRounds, StopAfterArenaRounds = settings.StopAfterArenaRounds, DoNotPurchaseArenaTicketsWithGold = settings.DoNotPurchaseArenaTicketsWithGold, StopAfterUndergroundArenaUnlocked = settings.StopAfterUndergroundArenaUnlocked, SpendAllGoldOnArenaTickets = settings.SpendAllGoldOnArenaTickets });
    }

    public BotSettings ApplyTo(BotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings, "settings");
        AccountModeRuntimeSettings accountModeRuntimeSettings = Normalize(this);
        return settings with
        {
            ConstructedAutoConcede = accountModeRuntimeSettings.ConstructedAutoConcede,
            ConstructedWinThenConcede = accountModeRuntimeSettings.ConstructedWinThenConcede,
            ConstructedWinsBeforeConcede = accountModeRuntimeSettings.ConstructedWinsBeforeConcede,
            ConstructedConcedesAfterWins = accountModeRuntimeSettings.ConstructedConcedesAfterWins,
            EnableConstructedRankControl = accountModeRuntimeSettings.EnableConstructedRankControl,
            ConstructedRankControlTarget = accountModeRuntimeSettings.ConstructedRankControlTarget,
            EnableNeteaseBoxLowWinRateAutoConcede = accountModeRuntimeSettings.EnableNeteaseBoxLowWinRateAutoConcede,
            EnableArenaAi2LowWinRateAutoConcede = accountModeRuntimeSettings.EnableArenaAi2LowWinRateAutoConcede,
            ArenaAi2LowWinRateConcedeThresholdPercent = accountModeRuntimeSettings.ArenaAi2LowWinRateConcedeThresholdPercent,
            NeteaseBoxLowWinRateConcedeThresholdPercent = accountModeRuntimeSettings.NeteaseBoxLowWinRateConcedeThresholdPercent,
            EnableConstructedDeckRotation = accountModeRuntimeSettings.EnableConstructedDeckRotation,
            ConstructedDeckRotationInterval = accountModeRuntimeSettings.ConstructedDeckRotationInterval,
            ConstructedDeckRotationDeckNames = accountModeRuntimeSettings.ConstructedDeckRotationDeckNames,
            EnableStopAtConstructedRank = accountModeRuntimeSettings.EnableStopAtConstructedRank,
            StopAtConstructedRank = accountModeRuntimeSettings.StopAtConstructedRank,
            EnableStopAtLegendRank = accountModeRuntimeSettings.EnableStopAtLegendRank,
            StopAtLegendRank = accountModeRuntimeSettings.StopAtLegendRank,
            EnableNeteaseBoxConstructedAutoRecovery = accountModeRuntimeSettings.EnableNeteaseBoxConstructedAutoRecovery,
            EnableNeteaseConstructedStallConcedeProtection = accountModeRuntimeSettings.EnableNeteaseConstructedStallConcedeProtection,
            EnableHumanizedAutomation = accountModeRuntimeSettings.HumanizedAutomationEnabled,
            EnableStrongAiHumanizedPacing = accountModeRuntimeSettings.EnableStrongAiHumanizedPacing,
            NeteaseBoxConstructedRecoveryCooldownSeconds = accountModeRuntimeSettings.NeteaseBoxConstructedRecoveryCooldownSeconds,
            NeteaseBoxConstructedStallTimeoutSeconds = accountModeRuntimeSettings.NeteaseBoxConstructedStallTimeoutSeconds,
            EnableBattlegroundsQuickCombat = accountModeRuntimeSettings.EnableBattlegroundsQuickCombat,
            EnableBattlegroundsTimeGear = accountModeRuntimeSettings.EnableBattlegroundsTimeGear,
            BattlegroundsTimeGearMultiplier = accountModeRuntimeSettings.BattlegroundsTimeGearMultiplier,
            EnableBattlegroundsScoreControl = accountModeRuntimeSettings.EnableBattlegroundsScoreControl,
            BattlegroundsScoreTarget = accountModeRuntimeSettings.BattlegroundsScoreTarget,
            EnableNeteaseBoxBattlegroundsAutoRecovery = accountModeRuntimeSettings.EnableNeteaseBoxBattlegroundsAutoRecovery,
            NeteaseBoxBattlegroundsRecoveryCooldownSeconds = accountModeRuntimeSettings.NeteaseBoxBattlegroundsRecoveryCooldownSeconds,
            NeteaseBoxBattlegroundsStallTimeoutSeconds = accountModeRuntimeSettings.NeteaseBoxBattlegroundsStallTimeoutSeconds,
            EnableStopAfterArenaRounds = accountModeRuntimeSettings.EnableStopAfterArenaRounds,
            StopAfterArenaRounds = accountModeRuntimeSettings.StopAfterArenaRounds,
            DoNotPurchaseArenaTicketsWithGold = accountModeRuntimeSettings.DoNotPurchaseArenaTicketsWithGold,
            StopAfterUndergroundArenaUnlocked = accountModeRuntimeSettings.StopAfterUndergroundArenaUnlocked,
            SpendAllGoldOnArenaTickets = accountModeRuntimeSettings.SpendAllGoldOnArenaTickets
        };
    }

    public static AccountModeRuntimeSettings Normalize(AccountModeRuntimeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings, "settings");
        int num = Math.Clamp(settings.ConstructedDeckRotationInterval, 0, 999);
        return settings with
        {
            EnableHumanizedAutomation = settings.HumanizedAutomationEnabled,
            EnableStrongAiHumanizedPacing = settings.HumanizedAutomationEnabled,
            ArenaAi2LowWinRateConcedeThresholdPercent = Math.Clamp(settings.ArenaAi2LowWinRateConcedeThresholdPercent, 1, 100),
            ConstructedWinsBeforeConcede = Math.Clamp((settings.ConstructedWinsBeforeConcede <= 0) ? 1 : settings.ConstructedWinsBeforeConcede, 1, 999),
            ConstructedConcedesAfterWins = Math.Clamp((settings.ConstructedConcedesAfterWins <= 0) ? 1 : settings.ConstructedConcedesAfterWins, 1, 999),
            ConstructedRankControlTarget = ConstructedRankControlPolicy.NormalizeTarget(settings.ConstructedRankControlTarget),
            EnableConstructedDeckRotation = (settings.EnableConstructedDeckRotation && num > 0),
            ConstructedDeckRotationInterval = num,
            ConstructedDeckRotationDeckNames = NormalizeRotationDeckNames(settings.ConstructedDeckRotationDeckNames),
            NeteaseBoxLowWinRateConcedeThresholdPercent = BotSettingsPolicy.NormalizeNeteaseBoxLowWinRateConcedeThresholdPercent(settings.NeteaseBoxLowWinRateConcedeThresholdPercent),
            StopAtConstructedRank = AccountRunStopPolicy.NormalizeConstructedRankTarget(settings.StopAtConstructedRank),
            StopAtLegendRank = Math.Clamp(settings.StopAtLegendRank, 1, 1000000),
            NeteaseBoxConstructedRecoveryCooldownSeconds = BotSettingsPolicy.NormalizeNeteaseBoxRecoveryCooldownSeconds(settings.NeteaseBoxConstructedRecoveryCooldownSeconds),
            NeteaseBoxConstructedStallTimeoutSeconds = BotSettingsPolicy.NormalizeNeteaseBoxStallTimeoutSeconds(settings.NeteaseBoxConstructedStallTimeoutSeconds),
            BattlegroundsTimeGearMultiplier = Math.Clamp(settings.BattlegroundsTimeGearMultiplier, 1.0m, 8.0m),
            BattlegroundsScoreTarget = Math.Clamp(settings.BattlegroundsScoreTarget, 0, 50000),
            NeteaseBoxBattlegroundsRecoveryCooldownSeconds = BotSettingsPolicy.NormalizeNeteaseBoxRecoveryCooldownSeconds(settings.NeteaseBoxBattlegroundsRecoveryCooldownSeconds),
            NeteaseBoxBattlegroundsStallTimeoutSeconds = BotSettingsPolicy.NormalizeNeteaseBoxStallTimeoutSeconds(settings.NeteaseBoxBattlegroundsStallTimeoutSeconds),
            StopAfterArenaRounds = Math.Clamp(settings.StopAfterArenaRounds, 1, 999)
        };
    }

    public static string[] ParseRotationDeckNames(string? value)
    {
        return (
            from name in (value ?? "").Split(new char[7] { '\r', '\n', ',', '，', ';', '；', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            where name.Length > 0
            select name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string NormalizeRotationDeckNames(string? value)
    {
        return string.Join("，", ParseRotationDeckNames(value));
    }
}