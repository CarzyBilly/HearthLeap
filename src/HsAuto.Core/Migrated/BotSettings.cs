// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Configuration;
public sealed record BotSettings
{
    public int SettingsMigrationVersion { get; init; }
    public string HearthstoneRoot { get; init; } = "";
    public bool EnableStudioHardware { get; init; }
    public bool EnableStudioProxyPool { get; init; }
    public bool ArrangeConcurrentHearthstoneWindows { get; init; } = true;
    public bool EnableMaxConcurrentClients { get; init; } = true;
    public int? MaxConcurrentClients { get; init; }
    public string HearthstoneRegion { get; init; } = "CN";
    public string RunMode { get; init; } = "Battlegrounds";
    public string ChannelPrefix { get; init; } = "HsAutoBattlegrounds";
    public int PollIntervalMs { get; init; } = 800;
    public int BattlegroundsMaxMatchMinutes { get; init; } = 40;
    public int ConstructedMaxMatchMinutes { get; init; } = 10;
    public bool EnableStopAfterArenaRounds { get; init; }
    public int StopAfterArenaRounds { get; init; } = 10;
    public bool DoNotPurchaseArenaTicketsWithGold { get; init; }
    public bool StopAfterUndergroundArenaUnlocked { get; init; }
    public bool SpendAllGoldOnArenaTickets { get; init; }
    public bool EnableStopAtConstructedRank { get; init; }
    public string StopAtConstructedRank { get; init; } = "Diamond5";
    public bool EnableStopAtLegendRank { get; init; }
    public int StopAtLegendRank { get; init; } = 30000;
    public bool EnableStopAtGold { get; init; }
    public int StopAtGold { get; init; } = 1500;
    public bool EnableNeteaseBoxBattlegroundsAutoRecovery { get; init; }
    public bool EnableNeteaseBoxConstructedAutoRecovery { get; init; }
    public bool EnableNeteaseConstructedStallConcedeProtection { get; init; }
    public bool? EnableHumanizedAutomation { get; init; }
    public bool EnableStrongAiHumanizedPacing { get; init; } = true;
    public bool HumanizedAutomationEnabled => EnableHumanizedAutomation ?? EnableStrongAiHumanizedPacing;
    public int NeteaseBoxBattlegroundsRecoveryCooldownSeconds { get; init; } = 120;
    public int NeteaseBoxConstructedRecoveryCooldownSeconds { get; init; } = 120;
    public int NeteaseBoxBattlegroundsStallTimeoutSeconds { get; init; } = 30;
    public int NeteaseBoxConstructedStallTimeoutSeconds { get; init; } = 15;
    public int NeteaseBoxConstructedMaxRecoveryAttempts { get; init; } = 3;
    public int MatchmakingTimeoutMinutes { get; init; } = 5;
    public bool EnableClientDisconnectAutoRecovery { get; init; } = true;
    public int ClientDisconnectRecoveryDelaySeconds { get; init; } = 30;
    public bool WriteActions { get; init; } = true;
    public bool DryRun { get; init; }
    public bool AllowBuy { get; init; } = true;
    public bool AllowSell { get; init; } = true;
    public bool AllowSpells { get; init; } = true;
    public bool AllowUpgrade { get; init; } = true;
    public bool PrioritizeTavernUpgrade { get; init; } = true;
    public bool AllowDiscover { get; init; } = true;
    public bool AutoEndTurn { get; init; }
    public bool SpendAllGold { get; init; } = true;
    public bool AutoNavigateToBattlegrounds { get; init; } = true;
    public bool AutoClaimRewardTrack { get; init; } = true;
    public bool AutoManageQuests { get; init; } = true;
    public bool AutoClaimAchievements { get; init; } = true;
    public bool ShrinkHearthstoneWindowOnLaunch { get; init; }
    public bool EnableSoftwareAutoUpdate { get; init; } = true;
    public bool EnableBattleNetMaintenance { get; init; } = true;
    public bool StartWithWindows { get; init; } = true;
    public bool DisableBepInExSharedDiskLogging { get; init; }
    public bool EnableProcessUserUiExecution { get; init; } = true;
    public bool CaptureHearthstoneTraffic { get; init; }
    public string HearthstoneTrafficDatabasePath { get; init; } = "";
    public bool EnableReconnectDiagnostics { get; init; }
    public bool EnableStrongAiDiagnostics { get; init; }
    public bool EnableNeteaseBoxLegendRankPatch { get; init; }
    public bool EnableBattlegroundsQuickCombat { get; init; }
    public bool EnableBattlegroundsTimeGear { get; init; }
    public decimal BattlegroundsTimeGearMultiplier { get; init; } = 2.0m;
    public bool EnableBattlegroundsScoreControl { get; init; }
    public int BattlegroundsScoreTarget { get; init; } = 6000;
    public bool ExecuteExternalAutomation { get; init; } = true;
    public bool UseUnityBridgeState { get; init; } = true;
    public string UnityBridgePipeName { get; init; } = "HsAuto.UnityBridge";
    public int UnityBridgeProcessId { get; init; }
    public int TargetTavernTier { get; init; } = 6;
    public int MinimumGoldAfterUpgrade { get; init; }
    public int MinimumGoldAfterBuy { get; init; }
    public string StrategyName { get; init; } = "BuiltInHeuristic";
    public string? HttpStrategyEndpoint { get; init; }
    public bool ConstructedEnabled { get; init; }
    public string ConstructedFormat { get; init; } = "Standard";
    public string ConstructedDeckName { get; init; } = "";
    public int? ConstructedDeckIndex { get; init; }
    public string ConstructedStrategyName { get; init; } = "BuiltInConstructedBasic";
    public string ArenaStrategyName { get; init; } = "NeteaseBoxArena";
    public bool ConstructedDryRun { get; init; }
    public bool ConstructedAutoQueue { get; init; } = true;
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
    public Dictionary<string, AccountModeRuntimeSettings> AccountModeSettings { get; init; } = new Dictionary<string, AccountModeRuntimeSettings>(StringComparer.Ordinal);
    public bool NewcomerTutorialEnabled { get; init; }
    public bool AutoNavigateToConstructed { get; init; } = true;
    public bool AutoNavigateToArena { get; init; } = true;
    public string AutomationProfilePath { get; init; } = "automation-profile.json";
    public bool EnableRunSchedule { get; init; } = true;
    public bool AutoStartWithinSchedule { get; init; }
    public string[] ScheduleArmedAccountIds { get; init; } = Array.Empty<string>();
    public string[] AccountGridColumnOrder { get; init; } = Array.Empty<string>();
    public string RunStartTime { get; init; } = "08:00:00";
    public string RunEndTime { get; init; } = "23:00:00";
    public bool EnableWatchdog { get; init; } = true;
    public int WatchdogHeartbeatTimeoutSeconds { get; init; } = 240;
    public int WatchdogRepeatedLoopLimit { get; init; } = 60;

    public const int CurrentSettingsMigrationVersion = 1;
    public const int DefaultNeteaseBoxRecoveryCooldownSeconds = 120;
    public const int MinimumNeteaseBoxRecoveryCooldownSeconds = 15;
    public const int MaximumNeteaseBoxRecoveryCooldownSeconds = 3600;
    public const int DefaultNeteaseBoxBattlegroundsStallTimeoutSeconds = 30;
    public const int DefaultNeteaseBoxConstructedStallTimeoutSeconds = 15;
    public const int MinimumNeteaseBoxStallTimeoutSeconds = 5;
    public const int MaximumNeteaseBoxStallTimeoutSeconds = 300;
    public const int DefaultNeteaseBoxConstructedMaxRecoveryAttempts = 3;
    public const int MinimumNeteaseBoxConstructedMaxRecoveryAttempts = 1;
    public const int MaximumNeteaseBoxConstructedMaxRecoveryAttempts = 10;
    public const int DefaultNeteaseBoxLowWinRateConcedeThresholdPercent = 20;
    public const int MinimumNeteaseBoxLowWinRateConcedeThresholdPercent = 1;
    public const int MaximumNeteaseBoxLowWinRateConcedeThresholdPercent = 100;
    public const int DefaultClientDisconnectRecoveryDelaySeconds = 30;
    public const int MinimumClientDisconnectRecoveryDelaySeconds = 5;
    public const int MaximumClientDisconnectRecoveryDelaySeconds = 3600;
    public const int DefaultConstructedWinsBeforeConcede = 1;
    public const int DefaultConstructedConcedesAfterWins = 1;
    public const int MinimumConstructedWinConcedeCount = 1;
    public const int MaximumConstructedWinConcedeCount = 999;
    public const string DefaultChannelPrefix = "HsAutoBattlegrounds";
}