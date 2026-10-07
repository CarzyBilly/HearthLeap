// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HsAuto.Core.Configuration;
public static class BotSettingsPolicy
{
    public static BotSettings ApplyRequiredDefaults(BotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings, "settings");
        BotSettings botSettings = new BotSettings();
        bool flag = settings.SettingsMigrationVersion < 1;
        bool flag2 = flag || settings.HumanizedAutomationEnabled;
        Dictionary<string, AccountModeRuntimeSettings> dictionary = AccountModeRuntimeSettingsResolver.Normalize(settings.AccountModeSettings);
        if (flag)
        {
            string[] array = dictionary.Keys.ToArray();
            foreach (string key in array)
            {
                dictionary[key] = AccountModeRuntimeSettings.Normalize(dictionary[key] with { EnableHumanizedAutomation = true, EnableStrongAiHumanizedPacing = true });
            }
        }

        return settings with
        {
            SettingsMigrationVersion = Math.Max(settings.SettingsMigrationVersion, 1),
            EnableHumanizedAutomation = flag2,
            EnableStrongAiHumanizedPacing = flag2,
            EnableReconnectDiagnostics = false,
            EnableStrongAiDiagnostics = false,
            DryRun = false,
            AllowBuy = true,
            AllowSell = true,
            AllowSpells = true,
            AllowUpgrade = true,
            AllowDiscover = true,
            AutoEndTurn = false,
            SpendAllGold = true,
            AutoNavigateToBattlegrounds = true,
            ExecuteExternalAutomation = true,
            UseUnityBridgeState = true,
            TargetTavernTier = botSettings.TargetTavernTier,
            PollIntervalMs = botSettings.PollIntervalMs,
            ConstructedDeckRotationInterval = Math.Clamp(settings.ConstructedDeckRotationInterval, 0, 999),
            EnableConstructedDeckRotation = (settings.EnableConstructedDeckRotation && settings.ConstructedDeckRotationInterval > 0),
            ConstructedDeckRotationDeckNames = string.Join("，", AccountModeRuntimeSettings.ParseRotationDeckNames(settings.ConstructedDeckRotationDeckNames)),
            AccountModeSettings = dictionary,
            BattlegroundsTimeGearMultiplier = Math.Clamp(settings.BattlegroundsTimeGearMultiplier, 1.0m, 8.0m),
            BattlegroundsScoreTarget = Math.Clamp(settings.BattlegroundsScoreTarget, 0, 50000),
            StopAtConstructedRank = AccountRunStopPolicy.NormalizeConstructedRankTarget(settings.StopAtConstructedRank),
            ConstructedWinsBeforeConcede = Math.Clamp((settings.ConstructedWinsBeforeConcede <= 0) ? 1 : settings.ConstructedWinsBeforeConcede, 1, 999),
            ConstructedConcedesAfterWins = Math.Clamp((settings.ConstructedConcedesAfterWins <= 0) ? 1 : settings.ConstructedConcedesAfterWins, 1, 999),
            ConstructedRankControlTarget = ConstructedRankControlPolicy.NormalizeTarget(settings.ConstructedRankControlTarget),
            StopAtLegendRank = Math.Clamp(settings.StopAtLegendRank, 1, 1000000),
            StopAtGold = Math.Clamp(settings.StopAtGold, 0, 1000000),
            MatchmakingTimeoutMinutes = Math.Clamp(settings.MatchmakingTimeoutMinutes, 1, 60),
            ClientDisconnectRecoveryDelaySeconds = NormalizeClientDisconnectRecoveryDelaySeconds(settings.ClientDisconnectRecoveryDelaySeconds),
            NeteaseBoxBattlegroundsRecoveryCooldownSeconds = NormalizeNeteaseBoxRecoveryCooldownSeconds(settings.NeteaseBoxBattlegroundsRecoveryCooldownSeconds),
            NeteaseBoxConstructedRecoveryCooldownSeconds = NormalizeNeteaseBoxRecoveryCooldownSeconds(settings.NeteaseBoxConstructedRecoveryCooldownSeconds),
            NeteaseBoxBattlegroundsStallTimeoutSeconds = NormalizeNeteaseBoxStallTimeoutSeconds(settings.NeteaseBoxBattlegroundsStallTimeoutSeconds),
            NeteaseBoxConstructedStallTimeoutSeconds = NormalizeNeteaseBoxStallTimeoutSeconds(settings.NeteaseBoxConstructedStallTimeoutSeconds),
            NeteaseBoxConstructedMaxRecoveryAttempts = NormalizeNeteaseBoxConstructedMaxRecoveryAttempts(settings.NeteaseBoxConstructedMaxRecoveryAttempts),
            NeteaseBoxLowWinRateConcedeThresholdPercent = NormalizeNeteaseBoxLowWinRateConcedeThresholdPercent(settings.NeteaseBoxLowWinRateConcedeThresholdPercent),
            ArenaAi2LowWinRateConcedeThresholdPercent = Math.Clamp(settings.ArenaAi2LowWinRateConcedeThresholdPercent, 1, 100),
            EnableRunSchedule = true,
            AutoStartWithinSchedule = false,
            EnableWatchdog = true,
            WatchdogHeartbeatTimeoutSeconds = botSettings.WatchdogHeartbeatTimeoutSeconds,
            WatchdogRepeatedLoopLimit = botSettings.WatchdogRepeatedLoopLimit
        };
    }

    public static int NormalizeNeteaseBoxRecoveryCooldownSeconds(int seconds)
    {
        return Math.Clamp(seconds, 15, 3600);
    }

    public static int NormalizeClientDisconnectRecoveryDelaySeconds(int seconds)
    {
        return Math.Clamp(seconds, 5, 3600);
    }

    public static int NormalizeNeteaseBoxStallTimeoutSeconds(int seconds)
    {
        return Math.Clamp(seconds, 5, 300);
    }

    public static int NormalizeNeteaseBoxConstructedMaxRecoveryAttempts(int attempts)
    {
        return Math.Clamp(attempts, 1, 10);
    }

    public static int NormalizeNeteaseBoxLowWinRateConcedeThresholdPercent(int percent)
    {
        return Math.Clamp(percent, 1, 100);
    }
}