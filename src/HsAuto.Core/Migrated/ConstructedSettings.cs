// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using HsAuto.Core.Models;

namespace HsAuto.Core.Configuration;
public sealed record ConstructedSettings
{
    public ConstructedFormat Format { get; init; }
    public string DeckName { get; init; } = "";
    public int? DeckIndex { get; init; }
    public string StrategyName { get; init; } = "BuiltInConstructedBasic";
    public bool DryRun { get; init; }
    public bool AutoQueue { get; init; } = true;
    public bool AutoClaimRewardTrack { get; init; } = true;
    public bool AutoClaimAchievements { get; init; } = true;
    public bool AutoConcede { get; init; }
    public bool WinThenConcede { get; init; }
    public int WinsBeforeConcede { get; init; } = 1;
    public int ConcedesAfterWins { get; init; } = 1;
    public bool RankControlConcedeNextMatch { get; init; }
    public bool EnableNeteaseBoxLowWinRateAutoConcede { get; init; }
    public bool EnableArenaAi2LowWinRateAutoConcede { get; init; }
    public int ArenaAi2LowWinRateConcedeThresholdPercent { get; init; } = 20;
    public int NeteaseBoxLowWinRateConcedeThresholdPercent { get; init; } = 20;
    public bool NewcomerTutorialEnabled { get; init; }
    public int PollIntervalMs { get; init; } = 800;
    public int MaxMatchMinutes { get; init; } = 10;
    public bool EnableNeteaseBoxAutoRecovery { get; init; }
    public bool EnableNeteaseStallConcedeProtection { get; init; }
    public bool EnableHumanizedAutomation { get; init; } = true;
    public bool EnableStrongAiHumanizedPacing => EnableHumanizedAutomation;
    public int NeteaseBoxRecoveryCooldownSeconds { get; init; } = 120;
    public int NeteaseBoxStallTimeoutSeconds { get; init; } = 15;
    public int NeteaseBoxMaxRecoveryAttempts { get; init; } = 3;

    public static ConstructedSettings FromBotSettings(BotSettings settings)
    {
        return new ConstructedSettings
        {
            Format = ParseFormat(settings.ConstructedFormat),
            DeckName = (settings.ConstructedDeckName?.Trim() ?? ""),
            DeckIndex = settings.ConstructedDeckIndex,
            StrategyName = (string.IsNullOrWhiteSpace(settings.ConstructedStrategyName) ? "BuiltInConstructedBasic" : settings.ConstructedStrategyName.Trim()),
            DryRun = settings.ConstructedDryRun,
            AutoQueue = settings.ConstructedAutoQueue,
            AutoClaimRewardTrack = settings.AutoClaimRewardTrack,
            AutoClaimAchievements = settings.AutoClaimAchievements,
            AutoConcede = settings.ConstructedAutoConcede,
            WinThenConcede = settings.ConstructedWinThenConcede,
            WinsBeforeConcede = Math.Clamp((settings.ConstructedWinsBeforeConcede <= 0) ? 1 : settings.ConstructedWinsBeforeConcede, 1, 999),
            ConcedesAfterWins = Math.Clamp((settings.ConstructedConcedesAfterWins <= 0) ? 1 : settings.ConstructedConcedesAfterWins, 1, 999),
            EnableNeteaseBoxLowWinRateAutoConcede = settings.EnableNeteaseBoxLowWinRateAutoConcede,
            EnableArenaAi2LowWinRateAutoConcede = (ScriptRunMode.IsArena(settings.RunMode) && settings.ArenaStrategyName == "NeteaseDirectArena" && settings.EnableArenaAi2LowWinRateAutoConcede),
            ArenaAi2LowWinRateConcedeThresholdPercent = Math.Clamp(settings.ArenaAi2LowWinRateConcedeThresholdPercent, 1, 100),
            NeteaseBoxLowWinRateConcedeThresholdPercent = BotSettingsPolicy.NormalizeNeteaseBoxLowWinRateConcedeThresholdPercent(settings.NeteaseBoxLowWinRateConcedeThresholdPercent),
            NewcomerTutorialEnabled = settings.NewcomerTutorialEnabled,
            PollIntervalMs = settings.PollIntervalMs,
            MaxMatchMinutes = settings.ConstructedMaxMatchMinutes,
            EnableNeteaseBoxAutoRecovery = settings.EnableNeteaseBoxConstructedAutoRecovery,
            EnableNeteaseStallConcedeProtection = settings.EnableNeteaseConstructedStallConcedeProtection,
            EnableHumanizedAutomation = settings.HumanizedAutomationEnabled,
            NeteaseBoxRecoveryCooldownSeconds = BotSettingsPolicy.NormalizeNeteaseBoxRecoveryCooldownSeconds(settings.NeteaseBoxConstructedRecoveryCooldownSeconds),
            NeteaseBoxStallTimeoutSeconds = BotSettingsPolicy.NormalizeNeteaseBoxStallTimeoutSeconds(settings.NeteaseBoxConstructedStallTimeoutSeconds),
            NeteaseBoxMaxRecoveryAttempts = BotSettingsPolicy.NormalizeNeteaseBoxConstructedMaxRecoveryAttempts(settings.NeteaseBoxConstructedMaxRecoveryAttempts)
        };
    }

    public static ConstructedFormat ParseFormat(string? value)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "wild":
            case "狂野":
            case "狂野模式":
                return ConstructedFormat.Wild;
            case "casual":
            case "休闲":
            case "休闲模式":
                return ConstructedFormat.Casual;
            default:
                return ConstructedFormat.Standard;
        }
    }

    public static string FormatToSettingValue(ConstructedFormat format)
    {
        return format switch
        {
            ConstructedFormat.Wild => "Wild",
            ConstructedFormat.Casual => "Casual",
            _ => "Standard",
        };
    }
}