// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;
using HsAuto.Core.Monitoring;

namespace HsAuto.Core.Automation;
public sealed class HearthstoneConstructedNavigator : IConstructedNavigator
{
    private enum DeckSelectionState
    {
        Unknown,
        Matched,
        Mismatched,
        Refreshing,
        NotFound
    }

    private const int MaxBattlegroundsEndGameDismissPolls = 80;
    private const int BattlegroundsEndGameRetryEveryPolls = 6;
    private const string MainMenuConstructedButtonPath = "/TheBox(Clone)/RootObject/TheBox_Outer/TheBox_CoverRight_mesh/TheBox_CenterDisk/TheBox_CenterDisk_mesh/FirstButton";
    private const string ConstructedPlayButtonPath = "/Tournament(Clone)/DeckPickerTray(Clone)/Hierarchy_Details/Button_Play";
    private const string ConstructedPlayButtonInteractionPath = "/Tournament(Clone)/DeckPickerTray(Clone)/Hierarchy_Details/Button_Play/Button_Play/InteractionController";
    private const string ConstructedSwitchFormatButtonPath = "/Tournament(Clone)/DeckPickerTray(Clone)/DeckPickerTrayFrame/SwitchFormatButtonContainer/SwitchFormatButton(Clone)/ButtonRoot";
    private const int MaxRewardPopupDismissAttempts = 180;
    private static readonly TimeSpan PostGameClickFallbackDelay = TimeSpan.FromSeconds(45.0);
    private static readonly TimeSpan PostGameClickFallbackCooldown = TimeSpan.FromSeconds(30.0);
    private static readonly TimeSpan NativeReconnectGrace = TimeSpan.FromSeconds(20.0);
    private static readonly string[] ControlClickComponents = new string[5]
    {
        "PegUIElement",
        "UIBButton",
        "BoxScrollButton",
        "PlayButton",
        "Clickable"
    };
    private readonly UnityBridgeClient _client;
    private readonly HearthstoneStartScreenController _startScreenController;
    private readonly HearthstoneMatchmakingMonitor _matchmakingMonitor;
    private readonly bool _enableStartScreenDismissal;
    private readonly HumanizedInteractionPacer? _humanizedInteractionPacer;
    private readonly Func<bool>? _humanizedAutomationEnabled;
    private readonly SemaphoreSlim _navigationGate = new SemaphoreSlim(1, 1);
    private readonly MenuNavigationStallTracker _mainMenuEntryStall = new MenuNavigationStallTracker();
    private readonly StartScreenDismissRecoveryTracker _startScreenDismissRecovery = new StartScreenDismissRecoveryTracker();
    private DateTimeOffset _lastStartScreenDismissAttemptAt = DateTimeOffset.MinValue;
    private DateTimeOffset _suppressOpenConstructedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _suppressStartMatchUntil = DateTimeOffset.MinValue;
    private string _lastSelectedDeckKey = "";
    private DateTimeOffset _lastSelectedDeckAt = DateTimeOffset.MinValue;
    private string _missingDeckKey = "";
    private DateTimeOffset _missingDeckSince = DateTimeOffset.MinValue;
    private DateTimeOffset _postGameBlockedSince = DateTimeOffset.MinValue;
    private DateTimeOffset _lastPostGameClickFallbackAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nativeReconnectAttemptedAt = DateTimeOffset.MinValue;
    private string? _blockingStatus;
    private int _connectionState;
    private bool _constructedLobbyObserved;
    private bool _hasQueuedMatchThisSession;
    private bool _hasObservedConstructedGameplayThisSession;
    private bool _autoQueuePausedAfterGameOver;
    private readonly MainMenuStartupCoordinator _mainMenuStartup;
    private bool _gameResultReportedForCurrentGame;
    private bool _constructedSettlementPending;
    private int _matchmakingActivityState;
    public ClientConnectionState ConnectionState => (ClientConnectionState)Volatile.Read(in _connectionState);

    public Func<CancellationToken, Task>? MainMenuStable
    {
        get
        {
            return _mainMenuStartup.MainMenuStable;
        }

        set
        {
            _mainMenuStartup.MainMenuStable = value;
        }
    }

    public Func<ConstructedSettings, CancellationToken, Task<MatchStartPreflightResult>>? BeforeMatchStartCheck { get; set; }
    public string? BlockingStatus => Volatile.Read(in _blockingStatus);
    public bool EnableStartScreenDismissal => _enableStartScreenDismissal;
    public string ConfirmedDeckName { get; private set; } = "";
    public bool HasObservedDeckPicker => _constructedLobbyObserved;
    public long MatchmakingObservationSequence => _matchmakingMonitor.MatchingObservationSequence;
    public long MatchStartRequestSequence => _matchmakingMonitor.MatchStartRequestSequence;
    public long PostGameTransitionProgressSequence => 0L;
    public MatchmakingActivityState MatchmakingActivityState => (MatchmakingActivityState)Volatile.Read(in _matchmakingActivityState);

    public event Action<string>? Log;
    public event Action<ConstructedGameResult>? GameCompleted;
    public event Action? ConstructedLobbyEntered;
    public HearthstoneConstructedNavigator(UnityBridgeClient client, int? processId = null, TimeSpan? matchmakingTimeout = null, bool enableStartScreenDismissal = true, Func<bool>? humanizedAutomationEnabled = null)
    {
        _client = client;
        _mainMenuStartup = new MainMenuStartupCoordinator(client);
        _enableStartScreenDismissal = enableStartScreenDismissal;
        _humanizedAutomationEnabled = humanizedAutomationEnabled;
        _humanizedInteractionPacer = ((humanizedAutomationEnabled == null) ? null : new HumanizedInteractionPacer());
        _startScreenController = new HearthstoneStartScreenController(processId);
        _matchmakingMonitor = new HearthstoneMatchmakingMonitor(client, matchmakingTimeout);
        _matchmakingMonitor.Log += LogMessage;
    }

    public Task<bool> TryPrepareConstructedAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        return TryPrepareConstructedCoreAsync(settings, confirmedGameOver: false, allowMatchStart: true, cancellationToken);
    }

    public Task<bool> TryPrepareDeckOnlyAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        return TryPrepareConstructedCoreAsync(settings, confirmedGameOver: false, allowMatchStart: false, cancellationToken);
    }

    public Task<bool> TryHandleConfirmedGameOverAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        return TryPrepareConstructedCoreAsync(settings, confirmedGameOver: true, allowMatchStart: true, cancellationToken);
    }

    private async Task<bool> TryPrepareConstructedCoreAsync(ConstructedSettings settings, bool confirmedGameOver, bool allowMatchStart, CancellationToken cancellationToken)
    {
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            if (await TryRecoverDisconnectedClientOnlyCoreAsync(cancellationToken))
            {
                return true;
            }

            if (confirmedGameOver)
            {
                _matchmakingMonitor.ResetForConfirmedGameOver();
                SetMatchmakingActivityState(MatchmakingActivityState.NotMatching);
            }
            else
            {
                MatchmakingMonitorResult matchmakingMonitorResult = await _matchmakingMonitor.CheckAndRecoverAsync(cancellationToken);
                if (matchmakingMonitorResult.IsMatching)
                {
                    _constructedLobbyObserved = false;
                    SetMatchmakingActivityState(MatchmakingActivityState.Matching);
                    SetBlockingStatus(matchmakingMonitorResult.RestartRequested ? ("匹配超时请求恢复：传统对战已连续匹配 " + HearthstoneMatchmakingMonitor.FormatElapsed(matchmakingMonitorResult.Elapsed) + "，重启炉石客户端和脚本") : ("传统对战匹配中 " + HearthstoneMatchmakingMonitor.FormatElapsed(matchmakingMonitorResult.Elapsed)));
                    return matchmakingMonitorResult.RecoveryTriggered;
                }

                if (matchmakingMonitorResult.IsTransitioningToGame)
                {
                    _constructedLobbyObserved = false;
                    SetMatchmakingActivityState(MatchmakingActivityState.TransitioningToGame);
                    SetBlockingStatus("传统对战已匹配到对手，等待进入对局");
                    return false;
                }

                SetMatchmakingActivityState(MatchmakingActivityState.NotMatching);
            }

            if (settings.AutoQueue)
            {
                _autoQueuePausedAfterGameOver = false;
            }

            if (!confirmedGameOver)
            {
                if (await _mainMenuStartup.TryHandleAsync(claimRewards: false, LogMessage, cancellationToken))
                {
                    SetBlockingStatus(_mainMenuStartup.Status);
                    return true;
                }

                if (_mainMenuStartup.StartScreenVisible)
                {
                    await TryDismissStartScreenAsync(cancellationToken);
                    return true;
                }
            }

            if (await TryDismissConstructedEndGameScreenAsync(settings, cancellationToken))
            {
                await Task.Delay(900, cancellationToken);
                return true;
            }

            if (await TryDismissBattlegroundsEndGameScreenAsync(cancellationToken))
            {
                await Task.Delay(900, cancellationToken);
                return true;
            }

            if (await TryDismissNavigationPopupsAsync(cancellationToken))
            {
                await Task.Delay(800, cancellationToken);
                return true;
            }

            if (await TryDismissInGameMessageModalAsync(cancellationToken))
            {
                await Task.Delay(800, cancellationToken);
                return true;
            }

            if (await TryDismissRewardPopupsIfVisibleAsync(cancellationToken) > 0)
            {
                await Task.Delay(800, cancellationToken);
                return true;
            }

            if (await TryDismissStartScreenAsync(cancellationToken))
            {
                await Task.Delay(1200, cancellationToken);
                return true;
            }

            if (await IsActiveConstructedGameplayAsync(cancellationToken))
            {
                _constructedLobbyObserved = false;
                _mainMenuEntryStall.Reset();
                _hasObservedConstructedGameplayThisSession = true;
                ResetPostGameClickFallbackTracking();
                ClearBlockingStatus();
                return false;
            }

            if (await TryReturnFromExplicitOutOfMatchPageAsync(cancellationToken))
            {
                await Task.Delay(900, cancellationToken);
                return true;
            }

            if (!(await IsConstructedLobbyVisibleAsync(cancellationToken)))
            {
                _constructedLobbyObserved = false;
                if (await TryOpenConstructedAsync(cancellationToken))
                {
                    await Task.Delay(2200, cancellationToken);
                    return true;
                }

                return false;
            }

            _mainMenuEntryStall.Reset();
            if (!_constructedLobbyObserved)
            {
                _constructedLobbyObserved = true;
                ConstructedLobbyEntered?.Invoke();
            }

            if (await TrySwitchFormatAsync(settings.Format, cancellationToken))
            {
                await Task.Delay(1200, cancellationToken);
                return true;
            }

            if (await TrySelectDeckAsync(settings, cancellationToken))
            {
                await Task.Delay(700, cancellationToken);
                return true;
            }

            if (!allowMatchStart)
            {
                SetBlockingStatus("已确认目标传统套牌选中，未启动匹配");
                return false;
            }

            if (await TryStartConstructedMatchAsync(settings, cancellationToken))
            {
                await Task.Delay(1800, cancellationToken);
                return true;
            }

            return await TryPostGameClickFallbackIfStuckAsync(settings, cancellationToken);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async Task<bool> TryRecoverDisconnectedClientOnlyAsync(CancellationToken cancellationToken)
    {
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            return await TryRecoverDisconnectedClientOnlyCoreAsync(cancellationToken);
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    public async Task<bool> TryDismissPostGameLayerOnlyAsync(ConstructedSettings settings, CancellationToken cancellationToken, bool includeGenericRewardPopups = true)
    {
        await _navigationGate.WaitAsync(cancellationToken);
        try
        {
            if (await TryRecoverDisconnectedClientOnlyCoreAsync(cancellationToken))
            {
                return true;
            }

            if (await TryDismissConstructedEndGameScreenAsync(settings, cancellationToken))
            {
                return true;
            }

            if (await TryDismissBattlegroundsEndGameScreenAsync(cancellationToken))
            {
                return true;
            }

            bool flag = await TryDismissNavigationPopupsAsync(cancellationToken);
            if (!flag)
            {
                flag = await TryDismissInGameMessageModalAsync(cancellationToken);
            }

            bool flag2 = flag;
            if (!flag2)
            {
                bool flag3 = includeGenericRewardPopups;
                if (flag3)
                {
                    flag3 = await TryDismissRewardPopupsIfVisibleAsync(cancellationToken) > 0;
                }

                flag2 = flag3;
            }

            if (flag2)
            {
                return true;
            }

            return false;
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private async Task<bool> TryRecoverDisconnectedClientOnlyCoreAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("reconnectState", new { }, 1500, cancellationToken);
        if (!bridgeResponse.Ok)
        {
            return ConnectionState == ClientConnectionState.Disconnected || _nativeReconnectAttemptedAt != DateTimeOffset.MinValue;
        }

        if (!ReadBool(bridgeResponse.Data, "visible"))
        {
            if (ReadBool(bridgeResponse.Data, "networkLoggedIn"))
            {
                Volatile.Write(ref _connectionState, 1);
            }

            if (ConnectionState == ClientConnectionState.Disconnected)
            {
                return true;
            }

            _nativeReconnectAttemptedAt = DateTimeOffset.MinValue;
            ClearBlockingStatus();
            return false;
        }

        if (IsAuthoritativeClientDisconnect(bridgeResponse.Data))
        {
            Volatile.Write(ref _connectionState, 2);
            _nativeReconnectAttemptedAt = DateTimeOffset.MinValue;
            SetBlockingStatus("客户端离线，等待自动重启");
            LogMessage("检测到账号客户端已离线，将在运行设置指定的持续离线时间后重启客户端和脚本");
            return true;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_nativeReconnectAttemptedAt == DateTimeOffset.MinValue)
        {
            BridgeResponse bridgeResponse2 = await _client.SendAsync("attemptReconnect", new { }, 1500, cancellationToken);
            if (bridgeResponse2.Ok && !ReadBool(bridgeResponse2.Data, "visible"))
            {
                ClearBlockingStatus();
                return false;
            }

            if (bridgeResponse2.Ok && ReadBool(bridgeResponse2.Data, "attempted"))
            {
                _nativeReconnectAttemptedAt = now;
                SetBlockingStatus("等待客户端原生重连");
                LogMessage("检测到客户端离线页面，已点击炉石原生重新连接，暂不重启当前对局");
                return true;
            }
        }
        else if (now - _nativeReconnectAttemptedAt < NativeReconnectGrace)
        {
            SetBlockingStatus("等待客户端原生重连");
            return true;
        }

        _nativeReconnectAttemptedAt = DateTimeOffset.MinValue;
        Volatile.Write(ref _connectionState, 2);
        SetBlockingStatus("客户端离线，等待自动重启");
        LogMessage("客户端原生重连后离线页面仍未消失，将在运行设置指定的持续离线时间后重启客户端和脚本");
        return true;
    }

    private static bool IsAuthoritativeClientDisconnect(JsonElement data)
    {
        string text = ReadString(data, "state");
        string text2 = ReadString(data, "scanSource");
        if (!text.Contains("ERROR", StringComparison.OrdinalIgnoreCase) && !text.Contains("RESTART_REQUIRED", StringComparison.OrdinalIgnoreCase) && !text2.Equals("Network.IsLoggedIn()", StringComparison.OrdinalIgnoreCase))
        {
            return text2.Equals("Box.Get().GetState()", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public async Task<IReadOnlyList<string>> ReadVisibleDeckNamesAsync(ConstructedFormat format, CancellationToken cancellationToken)
    {
        IReadOnlyList<ConstructedRotationDeck> readOnlyList = await ReadDeckCatalogAsync(cancellationToken);
        if (readOnlyList.Count > 0)
        {
            return (
                from deck in readOnlyList
                where format == ConstructedFormat.Casual || deck.Format == format
                select deck.Name into name
                    where !string.IsNullOrWhiteSpace(name) && !IsUiChromeText(name)select name).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string name) => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        IReadOnlyList<string> readOnlyList2 = await ReadVisibleDeckNamesInternallyAsync(cancellationToken);
        if (readOnlyList2.Count > 0)
        {
            return readOnlyList2;
        }

        HashSet<string> names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        object[] array = new object[4]
        {
            new
            {
                nameContains = "Deck"
            },
            new
            {
                nameContains = "deck"
            },
            new
            {
                pathContains = "Deck"
            },
            new
            {
                textContains = "套牌"
            }
        };
        foreach (object selector in array)
        {
            try
            {
                BridgeResponse bridgeResponse = await _client.SendAsync("find", new { selector = selector, maxObjects = 120, includeComponents = true }, 8000, cancellationToken);
                if (!bridgeResponse.Ok)
                {
                    continue;
                }

                foreach (JsonElement item in EnumerateObjects(bridgeResponse.Data))
                {
                    if (!string.Equals(ReadString(item, "name"), "DeckName", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string[] array2 = new string[1]
                    {
                        ReadString(item, "text")
                    };
                    for (int num2 = 0; num2 < array2.Length; num2++)
                    {
                        string text = NormalizeDeckName(array2[num2]);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            names.Add(text);
                        }
                    }
                }
            }
            catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        return names.Where((string name) => !IsUiChromeText(name)).OrderBy((string name) => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<ConstructedRotationDeck>> ReadDeckCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("constructedVisibleDeckCatalog", new { }, 8000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                LogMessage("读取传统卡组目录失败：" + bridgeResponse.Error);
                return Array.Empty<ConstructedRotationDeck>();
            }

            JsonElement[] array = EnumerateDecks(bridgeResponse.Data).ToArray();
            foreach (JsonElement item in array.Where((JsonElement item) => !ConstructedDeckRotationPolicy.IsPlayableCatalogEntry(item)))
            {
                HearthstoneConstructedNavigator hearthstoneConstructedNavigator = this;
                string text = NormalizeDeckName(ReadString(item, "name"));
                string text2 = ReadString(item, "unavailableReason");
                hearthstoneConstructedNavigator.LogMessage("跳过不可用卡组“" + text + "”：" + ((text2 != null && text2.Length > 0) ? text2 : "尚未确认可匹配"));
            }

            ConstructedRotationDeck[] array2 = (
                from deck in array.Where(ConstructedDeckRotationPolicy.IsPlayableCatalogEntry).Select((JsonElement item) =>
                {
                    string text3 = NormalizeDeckName(ReadString(item, "name"));
                    ConstructedFormat? constructedFormat = (ReadBool(item, "isWild") ? new ConstructedFormat? (ConstructedFormat.Wild) : (ReadBool(item, "isStandard") ? new ConstructedFormat? (ConstructedFormat.Standard) : ((ConstructedFormat? )null)));
                    return (!string.IsNullOrWhiteSpace(text3) && constructedFormat.HasValue) ? new ConstructedRotationDeck(text3, constructedFormat.Value) : null;
                })
                where (object)deck != null
                select (deck)).DistinctBy((ConstructedRotationDeck deck) => deck.Name, StringComparer.OrdinalIgnoreCase).OrderBy((ConstructedRotationDeck deck) => deck.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            if (array.Any(ConstructedDeckRotationPolicy.IsPlayableCatalogEntry) && array2.Length == 0)
            {
                IEnumerable<string> values =
                    from item in array.Take(5)select NormalizeDeckName(ReadString(item, "name")) + ":" + ReadString(item, "formatType");
                LogMessage($"已读取 {array.Length} 副传统卡组，但客户端未返回卡组自身格式：" + string.Join("，", values));
            }

            return array2;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex2)
        {
            LogMessage("读取传统卡组目录异常：" + ex2.Message);
            return Array.Empty<ConstructedRotationDeck>();
        }
    }

    private async Task<IReadOnlyList<string>> ReadVisibleDeckNamesInternallyAsync(CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("constructedDecks", new { }, 8000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                return Array.Empty<string>();
            }

            return (
                from item in EnumerateDecks(bridgeResponse.Data)select NormalizeDeckName(ReadString(item, "name"))into name
                    where !string.IsNullOrWhiteSpace(name) && !IsUiChromeText(name)select name).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy((string name) => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private async Task<bool> TryOpenConstructedAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (now < _suppressOpenConstructedUntil)
        {
            return false;
        }

        var exactSelector = new
        {
            path = "/TheBox(Clone)/RootObject/TheBox_Outer/TheBox_CoverRight_mesh/TheBox_CenterDisk/TheBox_CenterDisk_mesh/FirstButton"
        };
        bool exactExists = await ExistsAsync(exactSelector, cancellationToken);
        var fallbackSelector = new
        {
            name = "FirstButton"
        };
        bool flag = exactExists;
        if (!flag)
        {
            flag = await ExistsAsync(fallbackSelector, cancellationToken);
        }

        if (!flag)
        {
            _mainMenuEntryStall.Reset();
            return false;
        }

        if (_mainMenuEntryStall.ShouldRequestRestart(now))
        {
            TimeSpan timeSpan = _mainMenuEntryStall.Elapsed(now);
            SetBlockingStatus("菜单入口长时间无响应，立即重启客户端（传统对战）");
            LogMessage($"传统对战入口已内部点击 {_mainMenuEntryStall.AttemptCount} 次，持续 {timeSpan.TotalSeconds:0} 秒仍停留主菜单；" + "判定炉石菜单模块初始化卡死，交给账号恢复流程重启同一客户端并续跑。");
            return true;
        }

        flag = exactExists;
        if (flag)
        {
            bool flag2 = await TryPressReleaseAsync(exactSelector, "BoxScrollButton", cancellationToken);
            if (!flag2)
            {
                flag2 = await ClickObjectAsync(exactSelector, cancellationToken);
            }

            flag = flag2;
        }

        if (flag)
        {
            _mainMenuEntryStall.RecordAttempt(now);
            _suppressOpenConstructedUntil = DateTimeOffset.Now.AddSeconds(10.0);
            SetBlockingStatus("等待进入传统对战");
            LogMessage("已点击主菜单传统对战入口");
            return true;
        }

        flag = await TryPressReleaseAsync(fallbackSelector, "BoxScrollButton", cancellationToken);
        if (!flag)
        {
            flag = await ClickObjectAsync(fallbackSelector, cancellationToken);
        }

        if (flag)
        {
            _mainMenuEntryStall.RecordAttempt(now);
            _suppressOpenConstructedUntil = DateTimeOffset.Now.AddSeconds(10.0);
            SetBlockingStatus("等待进入传统对战");
            LogMessage("已通过 FirstButton 进入传统对战");
            return true;
        }

        return false;
    }

    private async Task<bool> TrySwitchFormatAsync(ConstructedFormat targetFormat, CancellationToken cancellationToken)
    {
        string targetLabel = FormatLabel(targetFormat);
        if (await IsCurrentFormatVisibleAsync(targetFormat, cancellationToken))
        {
            return false;
        }

        if (await TrySwitchFormatInternallyAsync(targetFormat, targetLabel, cancellationToken))
        {
            return true;
        }

        if (!(await TryOpenFormatPickerAsync(targetFormat, targetLabel, cancellationToken)))
        {
            LogMessage("未打开传统对战模式选择，稍后重试：" + targetLabel);
            return false;
        }

        if (await TryClickFormatOptionAsync(targetFormat, targetLabel, cancellationToken))
        {
            return true;
        }

        LogMessage("未找到传统对战模式切换目标：" + targetLabel + "，稍后重试");
        return false;
    }

    private async Task<bool> TrySwitchFormatInternallyAsync(ConstructedFormat targetFormat, string targetLabel, CancellationToken cancellationToken)
    {
        _ = 1;
        try
        {
            if (!(await _client.SendAsync("constructedSwitchFormat", new { format = targetFormat.ToString(), visualsFormatType = ToVisualsFormatType(targetFormat) }, 8000, cancellationToken)).Ok)
            {
                return false;
            }

            await Task.Delay(700, cancellationToken);
            LogMessage("已通过内部方法切换传统对战模式：" + targetLabel);
            return true;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryOpenFormatPickerAsync(ConstructedFormat targetFormat, string targetLabel, CancellationToken cancellationToken)
    {
        if (await TryOpenFormatPickerInternallyAsync(targetLabel, cancellationToken))
        {
            return true;
        }

        object[] array = BuildFormatPickerButtonSelectors();
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken) && await TryClickControlAsync(selector, cancellationToken))
            {
                SetBlockingStatus("切换传统对战模式");
                await Task.Delay(500, cancellationToken);
                if (await IsFormatOptionVisibleAsync(targetFormat, cancellationToken))
                {
                    LogMessage("已打开传统对战模式选择，目标 " + targetLabel);
                    return true;
                }
            }
        }

        array = BuildFormatPickerProjectedClickSelectors();
        foreach (object selector2 in array)
        {
            if (await TryClickProjectedObjectAsync(selector2, "传统对战右上角模式徽章", cancellationToken))
            {
                SetBlockingStatus("切换传统对战模式");
                LogMessage("已点击传统对战模式徽章，目标 " + targetLabel);
                await Task.Delay(650, cancellationToken);
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryOpenFormatPickerInternallyAsync(string targetLabel, CancellationToken cancellationToken)
    {
        _ = 1;
        try
        {
            if (!(await _client.SendAsync("constructedOpenFormatPicker", new { }, 8000, cancellationToken)).Ok)
            {
                return false;
            }

            SetBlockingStatus("切换传统对战模式");
            LogMessage("已通过内部方法打开传统对战模式选择，目标 " + targetLabel);
            await Task.Delay(500, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryClickFormatOptionAsync(ConstructedFormat targetFormat, string targetLabel, CancellationToken cancellationToken)
    {
        object[] array = BuildFormatSelectors(targetFormat);
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken) && await TryClickControlAsync(selector, cancellationToken))
            {
                await Task.Delay(700, cancellationToken);
                if (await IsCurrentFormatVisibleAsync(targetFormat, cancellationToken))
                {
                    LogMessage("已切换传统对战模式：" + targetLabel);
                }
                else
                {
                    LogMessage("已点击传统对战模式选项：" + targetLabel);
                }

                return true;
            }
        }

        if (TryClickFormatOptionByRelativePosition(targetFormat))
        {
            await Task.Delay(900, cancellationToken);
            if (await IsCurrentFormatVisibleAsync(targetFormat, cancellationToken))
            {
                LogMessage("已切换传统对战模式：" + targetLabel);
            }
            else
            {
                LogMessage("已通过弹层相对位置点击传统模式：" + targetLabel);
            }

            return true;
        }

        return false;
    }

    private async Task<bool> IsFormatOptionVisibleAsync(ConstructedFormat format, CancellationToken cancellationToken)
    {
        object[] array = BuildFormatSelectors(format);
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TrySelectDeckAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        string deckKey = BuildDeckKey(settings);
        DeckSelectionState deckSelectionState = await ReadRequestedDeckSelectionStateAsync(settings, cancellationToken);
        if (deckSelectionState == DeckSelectionState.Refreshing)
        {
            _missingDeckKey = "";
            _missingDeckSince = DateTimeOffset.MinValue;
            ConfirmedDeckName = "";
            SetBlockingStatus("备阵套牌已领取，返回主界面后重新加载卡组列表");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(deckKey))
        {
            if (deckSelectionState == DeckSelectionState.NotFound)
            {
                if (_missingDeckKey != deckKey)
                {
                    _missingDeckKey = deckKey;
                    _missingDeckSince = DateTimeOffset.UtcNow;
                    LogMessage("等待卡组列表刷新后确认套牌：" + settings.DeckName);
                }

                if (DateTimeOffset.UtcNow - _missingDeckSince >= TimeSpan.FromSeconds(20.0))
                {
                    throw new ConstructedDeckNotFoundException(settings.DeckName);
                }

                SetBlockingStatus("等待卡组列表刷新");
                return true;
            }

            _missingDeckKey = "";
            _missingDeckSince = DateTimeOffset.MinValue;
            if (deckSelectionState == DeckSelectionState.Matched)
            {
                RememberSelectedDeck(deckKey);
                return false;
            }
        }

        if (await TrySelectDeckInternallyAsync(settings, deckKey, cancellationToken))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(settings.DeckName) && settings.DeckName.All(char.IsDigit))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(settings.DeckName))
        {
            foreach (JsonElement item in await FindObjectsAsync(new { textContains = settings.DeckName }, 20, includeComponents: false, cancellationToken))
            {
                if (!string.Equals(NormalizeDeckName(ReadString(item, "text")), NormalizeDeckName(settings.DeckName), StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                object obj = TryBuildDeckSelectorFromPath(ReadString(item, "path"));
                if (obj != null && await TryClickControlAsync(obj, cancellationToken))
                {
                    if (!(await ConfirmDeckSelectionAfterAttemptAsync(settings, deckKey, "已选择传统套牌：" + settings.DeckName, cancellationToken)))
                    {
                        LogMessage("已尝试选择传统套牌：" + settings.DeckName + "，但客户端当前选中态尚未确认，稍后重试");
                    }

                    return true;
                }
            }
        }

        if (settings.DeckIndex.HasValue && settings.DeckIndex.Value >= 0)
        {
            var selector = new
            {
                name = $"CollectionDeck(Clone) - {settings.DeckIndex.Value}"};
            bool flag = await ExistsAsync(selector, cancellationToken);
            if (flag)
            {
                flag = await TryClickControlAsync(selector, cancellationToken);
            }

            if (flag)
            {
                if (!(await ConfirmDeckSelectionAfterAttemptAsync(settings, deckKey, $"已按索引选择传统套牌：{settings.DeckIndex.Value + 1}", cancellationToken)))
                {
                    LogMessage($"已尝试按索引选择传统套牌：{settings.DeckIndex.Value + 1}，但客户端当前选中态尚未确认，稍后重试");
                }

                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryStartConstructedMatchAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.Now < _suppressStartMatchUntil)
        {
            return false;
        }

        if (!settings.AutoQueue && _autoQueuePausedAfterGameOver)
        {
            SetBlockingStatus("传统对局结束，已停止自动排队");
            return false;
        }

        string deckKey = BuildDeckKey(settings);
        if (!string.IsNullOrWhiteSpace(deckKey))
        {
            if (await ReadRequestedDeckSelectionStateAsync(settings, cancellationToken) != DeckSelectionState.Matched)
            {
                SetBlockingStatus("等待选择传统套牌");
                LogMessage("尚未确认已选择指定传统套牌，暂不开始匹配");
                return false;
            }

            RememberSelectedDeck(deckKey);
        }

        if (settings.AutoClaimRewardTrack)
        {
            AchievementClaimResult achievementClaimResult = await new PreMatchRewardCoordinator(_client).ClaimBeforeMatchAsync(LogMessage, cancellationToken);
            if (achievementClaimResult != AchievementClaimResult.Ready)
            {
                SetBlockingStatus((achievementClaimResult == AchievementClaimResult.Recover) ? "通行证奖励收尾超时请求恢复" : "等待匹配界面稳定");
                return true;
            }
        }

        if (settings.AutoClaimAchievements)
        {
            AchievementClaimResult achievementClaimResult2 = await new AchievementClaimCoordinator(_client).ClaimBeforeMatchAsync(LogMessage, cancellationToken);
            if (achievementClaimResult2 != AchievementClaimResult.Ready)
            {
                SetBlockingStatus((achievementClaimResult2 == AchievementClaimResult.Recover) ? "成就奖励收尾超时请求恢复" : "等待匹配界面稳定");
                return true;
            }
        }

        await DelayHumanizedMatchStartAsync(settings, cancellationToken);
        if (BeforeMatchStartCheck != null)
        {
            MatchStartPreflightResult matchStartPreflightResult = await BeforeMatchStartCheck(settings, cancellationToken);
            if (matchStartPreflightResult.Decision != MatchStartPreflightDecision.Allow)
            {
                SetBlockingStatus(matchStartPreflightResult.Message);
                if (matchStartPreflightResult.ShouldLog && !string.IsNullOrWhiteSpace(matchStartPreflightResult.Message))
                {
                    LogMessage(matchStartPreflightResult.Message + (string.IsNullOrEmpty(matchStartPreflightResult.Diagnostic) ? "" : ("；" + matchStartPreflightResult.Diagnostic)));
                }

                return true;
            }
        }

        object[] array = new object[9]
        {
            new
            {
                path = "/Tournament(Clone)/DeckPickerTray(Clone)/Hierarchy_Details/Button_Play/Button_Play/InteractionController"
            },
            new
            {
                path = "/Tournament(Clone)/DeckPickerTray(Clone)/Hierarchy_Details/Button_Play"
            },
            new
            {
                path = "/Tournament(Clone)/DeckPickerTray(Clone)/Hierarchy_Details/Button_Play/Button_Play"
            },
            new
            {
                nameContains = "PlayButton"
            },
            new
            {
                nameContains = "StartButton"
            },
            new
            {
                name = "Button_Play"
            },
            new
            {
                component = "PlayButton"
            },
            new
            {
                textContains = "开始"
            },
            new
            {
                textContains = "Play"
            }
        };
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken) && await TryClickControlAsync(selector, cancellationToken))
            {
                _hasQueuedMatchThisSession = true;
                _autoQueuePausedAfterGameOver = false;
                _suppressStartMatchUntil = DateTimeOffset.Now.AddSeconds(20.0);
                _matchmakingMonitor.MarkMatchStartRequested();
                SetMatchmakingActivityState(MatchmakingActivityState.Matching);
                SetBlockingStatus("传统对战匹配中");
                LogMessage("已点击传统对战开始按钮，进入匹配");
                return true;
            }
        }

        return false;
    }

    private async Task DelayHumanizedMatchStartAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        if (settings.EnableHumanizedAutomation && _humanizedInteractionPacer != null)
        {
            Func<bool>? humanizedAutomationEnabled = _humanizedAutomationEnabled;
            if (humanizedAutomationEnabled != null && humanizedAutomationEnabled())
            {
                HumanizedActionCategory category = (_hasQueuedMatchThisSession ? HumanizedActionCategory.PostGameQueue : HumanizedActionCategory.MatchStart);
                int value = await _humanizedInteractionPacer.DelayAsync(category, enabled: true, cancellationToken);
                LogMessage((category == HumanizedActionCategory.PostGameQueue) ? $"传统盒子AI拟人局间休息 {value} 毫秒后开始下一局传统对战" : $"传统盒子AI拟人观察套牌大厅 {value} 毫秒后开始传统对战匹配");
            }
        }
    }

    private async Task<bool> TrySelectDeckInternallyAsync(ConstructedSettings settings, string deckKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.DeckName) && (!settings.DeckIndex.HasValue || settings.DeckIndex.Value < 0))
        {
            return false;
        }

        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("constructedSelectDeck", new { deckName = settings.DeckName, deckIndex = (settings.DeckIndex ?? (-1)) }, 8000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                return false;
            }

            string selectedName = ReadString(bridgeResponse.Data.TryGetProperty("deck", out var value) ? value : bridgeResponse.Data, "name");
            if (!(await ConfirmDeckSelectionAfterAttemptAsync(settings, deckKey, (!string.IsNullOrWhiteSpace(selectedName)) ? ("已通过内部方法选择传统套牌：" + selectedName) : "已通过内部方法选择传统套牌", cancellationToken)))
            {
                LogMessage((!string.IsNullOrWhiteSpace(selectedName)) ? ("已通过内部方法尝试选择传统套牌：" + selectedName + "，但客户端当前选中态尚未确认，稍后重试") : "已通过内部方法尝试选择传统套牌，但客户端当前选中态尚未确认，稍后重试");
            }

            return true;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ConfirmDeckSelectionAfterAttemptAsync(ConstructedSettings settings, string deckKey, string successMessage, CancellationToken cancellationToken)
    {
        await Task.Delay(350, cancellationToken);
        if (await ReadRequestedDeckSelectionStateAsync(settings, cancellationToken) != DeckSelectionState.Matched)
        {
            return false;
        }

        RememberSelectedDeck(deckKey);
        LogMessage(successMessage);
        return true;
    }

    private async Task<DeckSelectionState> ReadRequestedDeckSelectionStateAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("constructedDecks", new { }, 8000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                return DeckSelectionState.Unknown;
            }

            JsonElement[] array = EnumerateDecks(bridgeResponse.Data).ToArray();
            if (bridgeResponse.Data.TryGetProperty("pagination", out var value) && ReadBool(value, "leftClaimedLoanerPage"))
            {
                LogMessage("备阵套牌已领取，已离开活动页面；重新进入传统模式刷新正式卡组列表");
                return DeckSelectionState.Refreshing;
            }

            if (array.Length == 0)
            {
                return DeckSelectionState.Unknown;
            }

            if (!string.IsNullOrWhiteSpace(settings.DeckName) && !array.Any((JsonElement deck) => IsRequestedDeckPayload(settings, deck)))
            {
                return DeckSelectionState.NotFound;
            }

            JsonElement[] array2 = array.Where((JsonElement deck) => ReadBool(deck, "selected")).ToArray();
            if (array2.Length != 1)
            {
                return DeckSelectionState.Unknown;
            }

            if (!IsRequestedDeckPayload(settings, array2[0]))
            {
                return DeckSelectionState.Mismatched;
            }

            ConfirmedDeckName = NormalizeDeckName(ReadString(array2[0], "name"));
            return DeckSelectionState.Matched;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return DeckSelectionState.Unknown;
        }
    }

    private bool HasFreshSelectedDeck(string deckKey)
    {
        if (string.Equals(deckKey, _lastSelectedDeckKey, StringComparison.Ordinal))
        {
            return DateTimeOffset.Now - _lastSelectedDeckAt < TimeSpan.FromSeconds(90.0);
        }

        return false;
    }

    private async Task<bool> TryDismissStartScreenAsync(CancellationToken cancellationToken)
    {
        if (!_enableStartScreenDismissal)
        {
            _startScreenDismissRecovery.Reset();
            return false;
        }

        JsonElement? jsonElement = await ReadStartScreenStatusAsync(cancellationToken);
        if (!jsonElement.HasValue)
        {
            return false;
        }

        if (!ReadBool(jsonElement.Value, "visible"))
        {
            _startScreenDismissRecovery.Reset();
            return false;
        }

        if (!ReadBool(jsonElement.Value, "dismissable"))
        {
            SetBlockingStatus("等待启动按钮可点击或登录弹窗出现");
            return true;
        }

        if (DateTimeOffset.Now - _lastStartScreenDismissAttemptAt < TimeSpan.FromSeconds(2.0))
        {
            SetBlockingStatus("等待关闭启动页");
            return true;
        }

        _lastStartScreenDismissAttemptAt = DateTimeOffset.Now;
        BridgeResponse bridgeResponse = await _client.SendAsync("dismissStartScreen", new { force = false }, 8000, cancellationToken);
        if (!bridgeResponse.Ok)
        {
            SetBlockingStatus("等待关闭启动页");
            LogMessage("关闭启动页失败：" + bridgeResponse.Error);
            return true;
        }

        if (!ReadBool(bridgeResponse.Data, "dismissed"))
        {
            if (!ReadBool(bridgeResponse.Data, "visible"))
            {
                _startScreenDismissRecovery.Reset();
            }

            SetBlockingStatus("等待登录与开门流程继续");
            return true;
        }

        bool flag = _startScreenDismissRecovery.RecordSuccessfulAttempt();
        int successfulAttemptCount = _startScreenDismissRecovery.SuccessfulAttemptCount;
        LogMessage($"已尝试关闭炉石启动页（连续第 {successfulAttemptCount} 次）");
        if (flag)
        {
            SetBlockingStatus($"{"启动页关闭超限请求恢复"}：已尝试关闭炉石启动页 {successfulAttemptCount} 次，立即重启炉石客户端和脚本");
            LogMessage("关闭炉石启动页已超过 10 次，已请求重启当前账号的炉石客户端和脚本");
            return true;
        }

        SetBlockingStatus("等待关闭启动页");
        return true;
    }

    private async Task<JsonElement?> ReadStartScreenStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("dismissStartScreen", new { dryRun = true }, 5000, cancellationToken);
            return bridgeResponse.Ok ? new JsonElement? (bridgeResponse.Data) : ((JsonElement? )null);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryDismissInGameMessageModalAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("dismissInGameMessageModal", new { }, 8000, cancellationToken);
        if (bridgeResponse.Ok && ReadBool(bridgeResponse.Data, "dismissed"))
        {
            SetBlockingStatus("关闭卡牌变动提示");
            LogMessage("已关闭卡牌变动提示弹窗");
            return true;
        }

        return false;
    }

    private async Task<bool> TryDismissNavigationPopupsAsync(CancellationToken cancellationToken)
    {
        int dismissedCount = 0;
        HashSet<string> kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("dismissNavigationPopup", new { }, 8000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                break;
            }

            if (!ReadBool(bridgeResponse.Data, "dismissed"))
            {
                if (ReadBool(bridgeResponse.Data, "visible"))
                {
                    LogMessage("检测到暂无法安全关闭的弹窗：" + ReadString(bridgeResponse.Data, "kind"));
                }

                break;
            }

            dismissedCount++;
            string text = ReadString(bridgeResponse.Data, "kind");
            if (!string.IsNullOrWhiteSpace(text))
            {
                kinds.Add(text);
            }

            SetBlockingStatus("关闭启动提示弹窗");
            await Task.Delay(500, cancellationToken);
        }

        if (dismissedCount == 0)
        {
            return false;
        }

        LogMessage($"已安全关闭通用提示弹窗 {dismissedCount} 个（{string.Join("、", kinds)}）");
        return true;
    }

    private async Task<int> TryDismissRewardPopupsIfVisibleAsync(CancellationToken cancellationToken)
    {
        int dismissedCount = 0;
        int missingCount = 0;
        bool sawConfirmedAbsentState = false;
        bool sawVisibleState = false;
        for (int attempt = 0; attempt < 180; attempt++)
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("dismissRewardPopup", new { }, 8000, cancellationToken);
            if (!bridgeResponse.Ok)
            {
                return dismissedCount;
            }

            if (ReadBool(bridgeResponse.Data, "dismissed"))
            {
                sawVisibleState = true;
                dismissedCount++;
                missingCount = 0;
                SetBlockingStatus("关闭奖励弹窗");
                await Task.Delay(600, cancellationToken);
                continue;
            }

            if (ReadBool(bridgeResponse.Data, "visible"))
            {
                sawVisibleState = true;
                missingCount = 0;
                SetBlockingStatus("等待奖励弹窗动画");
                await Task.Delay(450, cancellationToken);
                continue;
            }

            if (!ReadBool(bridgeResponse.Data, "visible"))
            {
                sawConfirmedAbsentState = dismissedCount > 0;
            }

            missingCount++;
            if (missingCount >= 2)
            {
                break;
            }

            await Task.Delay(300, cancellationToken);
        }

        if ((dismissedCount > 0) & sawConfirmedAbsentState)
        {
            LogMessage($"已通过内部点击关闭奖励弹窗 {dismissedCount} 个，并确认弹层已消失");
            return dismissedCount;
        }

        if (dismissedCount > 0)
        {
            LogMessage($"奖励弹窗内部点击已提交 {dismissedCount} 次，但弹层仍可见，暂不计作关闭");
        }

        if (sawVisibleState)
        {
            SetBlockingStatus("等待奖励弹窗完全消失");
            return 1;
        }

        return 0;
    }

    private async Task<bool> TryDismissConstructedEndGameScreenAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        bool? flag = await ReadConstructedEndGameVisibilityAsync(cancellationToken);
        if (flag != true)
        {
            if (_constructedSettlementPending && flag == false)
            {
                CompleteConstructedSettlement(settings);
                return true;
            }

            return _constructedSettlementPending;
        }

        _constructedSettlementPending = true;
        SetBlockingStatus("等待关闭传统结算界面");
        BridgeResponse response = await _client.SendAsync("continueConstructedEndGame", new { }, 3000, cancellationToken);
        if (response.Ok && ReadBool(response.Data, "rewardPopupVisible"))
        {
            SetBlockingStatus("等待关闭通行证升级奖励");
            if (ReadBool(response.Data, "invoked"))
            {
                LogMessage("已优先关闭通行证升级奖励卷轴，等待奖励回调后继续结算");
                await Task.Delay(600, cancellationToken);
            }

            return true;
        }

        if (response.Ok && ReadBool(response.Data, "invoked"))
        {
            LogMessage(ReadBool(response.Data, "inputBlocked") ? "已通过结算总按钮推进奖励弹层，等待游戏完成奖励回调" : "已通过结算总按钮推进传统胜负及星级结算");
            await Task.Delay(600, cancellationToken);
        }

        bool flag2 = response.Ok;
        if (flag2)
        {
            flag2 = await ReadConstructedEndGameVisibilityAsync(cancellationToken) == false;
        }

        if (flag2)
        {
            CompleteConstructedSettlement(settings);
        }

        return true;
    }

    private void CompleteConstructedSettlement(ConstructedSettings settings)
    {
        _constructedSettlementPending = false;
        ClearBlockingStatus();
        ResetPostGameClickFallbackTracking();
        LogMessage("已确认传统胜负及奖励结算全部关闭");
        PauseAutoQueueAfterGameOverIfNeeded(settings);
    }

    private async Task<bool> TryDismissBattlegroundsEndGameScreenAsync(CancellationToken cancellationToken)
    {
        if (await ReadBattlegroundsEndGameVisibilityAsync(cancellationToken) != true)
        {
            return false;
        }

        SetBlockingStatus("等待关闭战棋结算界面");
        for (int attempt = 0; attempt < 80; attempt++)
        {
            if (attempt % 6 == 0)
            {
                try
                {
                    await _client.SendAsync("continueBattlegroundsEndGame", new { }, 3000, cancellationToken);
                }
                catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                }
            }

            if (await ReadBattlegroundsEndGameVisibilityAsync(cancellationToken) == false)
            {
                ClearBlockingStatus();
                LogMessage("已关闭战棋名次结算页，继续进入传统模式");
                return true;
            }

            await Task.Delay(350, cancellationToken);
        }

        return false;
    }

    private async Task<bool?> ReadBattlegroundsEndGameVisibilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("battlegroundsEndGameState", new { }, 1500, cancellationToken);
            return bridgeResponse.Ok ? new bool? (ReadBool(bridgeResponse.Data, "visible")) : ((bool? )null);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> IsConstructedEndGameScreenVisibleAsync(CancellationToken cancellationToken)
    {
        return await ReadConstructedEndGameVisibilityAsync(cancellationToken) == true;
    }

    private async Task<bool?> ReadConstructedEndGameVisibilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("battlegroundsStateLite", new { maxCards = 120, mode = "Constructed" }, 10000, cancellationToken);
            if (bridgeResponse.Ok)
            {
                bool flag = ReadBool(bridgeResponse.Data, "hasConstructedEndGameScreen");
                if (flag)
                {
                    ReportConstructedResult(bridgeResponse.Data);
                }

                return flag;
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }

        return null;
    }

    private async Task<bool> TryReturnFromExplicitOutOfMatchPageAsync(CancellationToken cancellationToken)
    {
        bool flag = await IsConstructedLobbyVisibleAsync(cancellationToken);
        if (!flag)
        {
            flag = await ExistsAsync(new { pathContains = "/Gameplay/" }, cancellationToken);
        }

        if (flag)
        {
            return false;
        }

        object[] array = new object[9]
        {
            new
            {
                path = "/Bacon(Clone)/BaconDisplay/BaconDisplay/Root/PlatformController/BaconLobby_PC/BaconLobby_PC/Button_Back/Button_Framed/ButtonFramed"
            },
            new
            {
                name = "DoneButton"
            },
            new
            {
                name = "ReturnButton"
            },
            new
            {
                name = "BackButton"
            },
            new
            {
                name = "Button_Back"
            },
            new
            {
                name = "CancelButton"
            },
            new
            {
                textContains = "返回"
            },
            new
            {
                textContains = "完成"
            },
            new
            {
                textContains = "继续"
            }
        };
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken) && await TryClickControlAsync(selector, cancellationToken))
            {
                SetBlockingStatus("等待返回传统对战");
                LogMessage("检测到对局外返回按钮，已点击返回传统对战");
                return true;
            }
        }

        return false;
    }

    private async Task<bool> TryPostGameClickFallbackIfStuckAsync(ConstructedSettings settings, CancellationToken cancellationToken)
    {
        bool isPostGameVisible = await IsConstructedEndGameScreenVisibleAsync(cancellationToken);
        bool flag = !isPostGameVisible;
        if (flag)
        {
            flag = await IsConstructedLobbyVisibleAsync(cancellationToken);
        }

        bool flag2 = flag;
        if (!isPostGameVisible && !flag2)
        {
            ResetPostGameClickFallbackTracking();
            return false;
        }

        DateTimeOffset now = DateTimeOffset.Now;
        if (_postGameBlockedSince == DateTimeOffset.MinValue)
        {
            _postGameBlockedSince = now;
            return false;
        }

        if (now - _postGameBlockedSince < PostGameClickFallbackDelay || now - _lastPostGameClickFallbackAt < PostGameClickFallbackCooldown)
        {
            return false;
        }

        flag = await TryDismissRewardPopupsIfVisibleAsync(cancellationToken) > 0;
        if (!flag)
        {
            flag = await TryDismissConstructedEndGameScreenAsync(settings, cancellationToken);
        }

        if (flag)
        {
            _lastPostGameClickFallbackAt = now;
            return true;
        }

        SetBlockingStatus("传统结算内部点击兜底");
        LogMessage("传统结算或大厅长时间未推进，执行 UnityBridge 内部点击兜底");
        bool clicked = false;
        object[] array = new object[6]
        {
            new
            {
                pathContains = "/RewardScroll Popup Bone/RewardScroll/Clickable"
            },
            new
            {
                pathContains = "BoosterPackReward/Root/ClickCatcher"
            },
            new
            {
                pathContains = "VictoryTwoScoop(Clone)/Hitbox"
            },
            new
            {
                pathContains = "DefeatTwoScoop(Clone)/Hitbox"
            },
            new
            {
                pathContains = "EndGameTwoScoop(Clone)/Hitbox"
            },
            new
            {
                nameContains = "RankChangeTwoScoop"
            }
        };
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken))
            {
                bool flag3 = clicked;
                clicked = flag3 | await TryClickControlAsync(selector, cancellationToken);
                await Task.Delay(220, cancellationToken);
            }
        }

        _lastPostGameClickFallbackAt = now;
        return clicked;
    }

    private void PauseAutoQueueAfterGameOverIfNeeded(ConstructedSettings settings)
    {
        if (!settings.AutoQueue && (_hasQueuedMatchThisSession || _hasObservedConstructedGameplayThisSession))
        {
            _autoQueuePausedAfterGameOver = true;
            SetBlockingStatus("传统对局结束，已停止自动排队");
            LogMessage("传统对局结束，按设置不继续下一局");
        }
    }

    private void ResetPostGameClickFallbackTracking()
    {
        _postGameBlockedSince = DateTimeOffset.MinValue;
        _lastPostGameClickFallbackAt = DateTimeOffset.MinValue;
    }

    private async Task<bool> IsConstructedLobbyVisibleAsync(CancellationToken cancellationToken)
    {
        object[] array = new object[7]
        {
            new
            {
                textContains = "标准模式"
            },
            new
            {
                textContains = "狂野模式"
            },
            new
            {
                textContains = "休闲模式"
            },
            new
            {
                textContains = "我的收藏"
            },
            new
            {
                nameContains = "DeckPicker"
            },
            new
            {
                nameContains = "DeckTray"
            },
            new
            {
                nameContains = "Ranked"
            }
        };
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> IsActiveConstructedGameplayAsync(CancellationToken cancellationToken)
    {
        _ = 1;
        try
        {
            BridgeResponse response = await _client.SendAsync("battlegroundsStateLite", new { maxCards = 2200, mode = "Constructed" }, 10000, cancellationToken);
            if (!response.Ok)
            {
                return false;
            }

            if (!(await ExistsAsync(new { pathContains = "/Gameplay/" }, cancellationToken)))
            {
                return false;
            }

            bool num = ReadInt(response.Data, "cardCount") > 0 && !ReadBool(response.Data, "isBattlegroundsCombatPhase") && !ReadBool(response.Data, "isBattlegroundsShopPhase") && !ReadBool(response.Data, "hasBattlegroundsEndGameScreen") && !ReadBool(response.Data, "hasConstructedEndGameScreen");
            if (num)
            {
                _gameResultReportedForCurrentGame = false;
            }

            return num;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsCurrentFormatVisibleAsync(ConstructedFormat format, CancellationToken cancellationToken)
    {
        object[] array = BuildCurrentFormatSelectors(format);
        foreach (object selector in array)
        {
            if (await ExistsAsync(selector, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static object[] BuildCurrentFormatSelectors(ConstructedFormat format)
    {
        return format switch
        {
            ConstructedFormat.Wild => new object[2]
            {
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "WildButton"
                },
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "WildButton_mesh"
                }
            },
            ConstructedFormat.Casual => new object[2]
            {
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "CasualButton"
                },
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "CasualButton_mesh"
                }
            },
            _ => new object[2]
            {
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "StandardButton"
                },
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "StandardButton_mesh"
                }
            },
        };
    }

    private static object[] BuildFormatPickerButtonSelectors()
    {
        return new object[7]
        {
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "StandardButton_mesh"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "WildButton_mesh"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "CasualButton_mesh"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "SetRotationIcon"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "IconYear_Quad"
            },
            new
            {
                path = "/Tournament(Clone)/DeckPickerTray(Clone)/DeckPickerTrayFrame/SwitchFormatButtonContainer/SwitchFormatButton(Clone)/ButtonRoot"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "ButtonRoot"
            }
        };
    }

    private static object[] BuildFormatPickerProjectedClickSelectors()
    {
        return new object[6]
        {
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "StandardButton_mesh"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "WildButton_mesh"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "CasualButton_mesh"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "SetRotationIcon"
            },
            new
            {
                pathContains = "SwitchFormatButton",
                nameContains = "IconYear_Quad"
            },
            new
            {
                path = "/Tournament(Clone)/DeckPickerTray(Clone)/DeckPickerTrayFrame/SwitchFormatButtonContainer/SwitchFormatButton(Clone)/ButtonRoot"
            }
        };
    }

    private static object[] BuildFormatSelectors(ConstructedFormat format)
    {
        return format switch
        {
            ConstructedFormat.Wild => new object[4]
            {
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "WildButton"
                },
                new
                {
                    pathContains = "FormatTypePickerPopup",
                    nameContains = "Wild"
                },
                new
                {
                    textContains = "狂野模式"
                },
                new
                {
                    textContains = "狂野"
                }
            },
            ConstructedFormat.Casual => new object[4]
            {
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "CasualButton"
                },
                new
                {
                    pathContains = "FormatTypePickerPopup",
                    nameContains = "Casual"
                },
                new
                {
                    textContains = "休闲模式"
                },
                new
                {
                    textContains = "休闲"
                }
            },
            _ => new object[4]
            {
                new
                {
                    pathContains = "SwitchFormatButton",
                    nameContains = "StandardButton"
                },
                new
                {
                    pathContains = "FormatTypePickerPopup",
                    nameContains = "Standard"
                },
                new
                {
                    textContains = "标准模式"
                },
                new
                {
                    textContains = "标准"
                }
            },
        };
    }

    private static object? TryBuildDeckSelectorFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        int num = path.IndexOf("/CollectionDeck(Clone) - ", StringComparison.OrdinalIgnoreCase);
        if (num < 0)
        {
            return null;
        }

        int num2 = num + 1;
        int num3 = path.IndexOf('/', num2);
        return new
        {
            path = ((num3 > num2) ? path.Substring(0, num3) : path)
        };
    }

    private static string BuildDeckKey(ConstructedSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.DeckName))
        {
            return "name:" + settings.DeckName.Trim();
        }

        if (!settings.DeckIndex.HasValue)
        {
            return "";
        }

        return "index:" + settings.DeckIndex.Value;
    }

    private static bool IsRequestedDeckPayload(ConstructedSettings settings, JsonElement deck)
    {
        if (!string.IsNullOrWhiteSpace(settings.DeckName))
        {
            string b = NormalizeDeckName(settings.DeckName);
            string a = NormalizeDeckName(ReadString(deck, "name"));
            string a2 = NormalizeDeckName(ReadString(deck, "normalizedName"));
            if (!string.Equals(a, b, StringComparison.CurrentCultureIgnoreCase))
            {
                return string.Equals(a2, b, StringComparison.CurrentCultureIgnoreCase);
            }

            return true;
        }

        if (!settings.DeckIndex.HasValue)
        {
            return false;
        }

        int value = settings.DeckIndex.Value;
        if (ReadInt(deck, "index") != value)
        {
            return ReadInt(deck, "displayIndex") == value + 1;
        }

        return true;
    }

    private void RememberSelectedDeck(string deckKey)
    {
        _lastSelectedDeckKey = deckKey;
        _lastSelectedDeckAt = DateTimeOffset.Now;
    }

    private async Task<bool> TryClickControlAsync(object selector, CancellationToken cancellationToken)
    {
        string[] controlClickComponents = ControlClickComponents;
        foreach (string componentContains in controlClickComponents)
        {
            if (await TryPressReleaseAsync(selector, componentContains, cancellationToken))
            {
                return true;
            }
        }

        return await ClickObjectAsync(selector, cancellationToken);
    }

    private async Task<bool> TryPressReleaseAsync(object selector, string componentContains, CancellationToken cancellationToken)
    {
        if (!(await _client.SendAsync("invokeMethod", new { selector = selector, componentContains = componentContains, method = "TriggerPress" }, 8000, cancellationToken)).Ok)
        {
            return false;
        }

        await Task.Delay(120, cancellationToken);
        return (await _client.SendAsync("invokeMethod", new { selector = selector, componentContains = componentContains, method = "TriggerRelease" }, 8000, cancellationToken)).Ok;
    }

    private async Task<bool> ClickObjectAsync(object selector, CancellationToken cancellationToken)
    {
        return (await _client.SendAsync("clickObject", new { selector }, 8000, cancellationToken)).Ok;
    }

    private async Task<bool> TryClickProjectedObjectAsync(object selector, string label, CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("projectObject", new { selector }, 8000, cancellationToken);
            if (!bridgeResponse.Ok || !ReadBool(bridgeResponse.Data, "inView") || !TryReadNormalizedPoint(bridgeResponse.Data, out var xRatio, out var yRatio))
            {
                return false;
            }

            if (!_startScreenController.TryClickRelative(xRatio, yRatio, 450))
            {
                return false;
            }

            LogMessage("已通过对象投影点击：" + label);
            return true;
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private bool TryClickFormatOptionByRelativePosition(ConstructedFormat format)
    {
        var(xRatio, yRatio) = format switch
        {
            ConstructedFormat.Wild => (0.29, 0.39),
            ConstructedFormat.Casual => (0.71, 0.39),
            _ => (0.5, 0.39),
        };
        return _startScreenController.TryClickRelative(xRatio, yRatio);
    }

    private async Task<bool> ExistsAsync(object selector, CancellationToken cancellationToken)
    {
        try
        {
            BridgeResponse bridgeResponse = await _client.SendAsync("find", new { selector = selector, maxObjects = 1, includeComponents = false }, 3500, cancellationToken);
            JsonElement value;
            return bridgeResponse.Ok && bridgeResponse.Data.TryGetProperty("count", out value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<JsonElement>> FindObjectsAsync(object selector, int maxObjects, bool includeComponents, CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("find", new { selector, maxObjects, includeComponents }, 8000, cancellationToken);
        return bridgeResponse.Ok ? EnumerateObjects(bridgeResponse.Data).ToArray() : Array.Empty<JsonElement>();
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("objects", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            yield return item;
        }
    }

    private static IEnumerable<JsonElement> EnumerateDecks(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("decks", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (JsonElement item in value.EnumerateArray())
        {
            yield return item;
        }
    }

    private static string NormalizeDeckName(string? value)
    {
        string text = (value ?? "").ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40)
        {
            return "";
        }

        string[] array = new string[4]
        {
            "<material=",
            "</material>",
            "Show ",
            "Choose "
        };
        foreach (string value2 in array)
        {
            if (text.Contains(value2, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
        }

        return text;
    }

    private static bool IsUiChromeText(string? value)
    {
        string text = value ?? "";
        if (!string.IsNullOrWhiteSpace(text) && !text.Contains("Button", StringComparison.OrdinalIgnoreCase) && !text.Contains("DeckPicker", StringComparison.OrdinalIgnoreCase) && !text.Contains("DeckTray", StringComparison.OrdinalIgnoreCase) && !text.Contains("Root", StringComparison.OrdinalIgnoreCase) && !text.Contains("Visual", StringComparison.OrdinalIgnoreCase) && !text.Contains("标准模式", StringComparison.OrdinalIgnoreCase) && !text.Contains("狂野模式", StringComparison.OrdinalIgnoreCase) && !text.Contains("休闲模式", StringComparison.OrdinalIgnoreCase) && !text.Contains("我的收藏", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(text, "^\\d+\\s*/\\s*\\d+$", RegexOptions.CultureInvariant);
        }

        return true;
    }

    private static string FormatLabel(ConstructedFormat format)
    {
        return format switch
        {
            ConstructedFormat.Wild => "狂野模式",
            ConstructedFormat.Casual => "休闲模式",
            _ => "标准模式",
        };
    }

    private void ReportConstructedResult(JsonElement data)
    {
        if (!_gameResultReportedForCurrentGame)
        {
            ConstructedGameResult constructedGameResult;
            switch (ReadString(data, "constructedResult").Trim().ToLowerInvariant())
            {
                case "win":
                case "won":
                case "victory":
                    constructedGameResult = ConstructedGameResult.Win;
                    break;
                case "loss":
                case "lost":
                case "defeat":
                case "conceded":
                    constructedGameResult = ConstructedGameResult.Loss;
                    break;
                case "tie":
                case "draw":
                case "tied":
                    constructedGameResult = ConstructedGameResult.Draw;
                    break;
                default:
                    constructedGameResult = ConstructedGameResult.Unknown;
                    break;
            }

            ConstructedGameResult constructedGameResult2 = constructedGameResult;
            if (constructedGameResult2 != ConstructedGameResult.Unknown)
            {
                _gameResultReportedForCurrentGame = true;
                GameCompleted?.Invoke(constructedGameResult2);
            }
        }
    }

    private static int ToVisualsFormatType(ConstructedFormat format)
    {
        return format switch
        {
            ConstructedFormat.Wild => 1,
            ConstructedFormat.Casual => 4,
            _ => 2,
        };
    }

    private static bool ReadBool(JsonElement data, string property)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static int ReadInt(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
        {
            return 0;
        }

        return value2;
    }

    private static string ReadString(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return value.GetString() ?? "";
    }

    private static bool TryReadNormalizedPoint(JsonElement data, out double xRatio, out double yRatio)
    {
        xRatio = 0.0;
        yRatio = 0.0;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("normalized", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryReadDouble(value, "x", out xRatio) && TryReadDouble(value, "y", out yRatio) && xRatio >= 0.0 && xRatio <= 1.0 && yRatio >= 0.0)
        {
            return yRatio <= 1.0;
        }

        return false;
    }

    private static bool TryReadDouble(JsonElement data, string property, out double number)
    {
        number = 0.0;
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out number);
        }

        return false;
    }

    private void SetBlockingStatus(string status)
    {
        Volatile.Write(ref _blockingStatus, status);
    }

    private void ClearBlockingStatus()
    {
        Volatile.Write(ref _blockingStatus, null);
    }

    private void SetMatchmakingActivityState(MatchmakingActivityState state)
    {
        Volatile.Write(ref _matchmakingActivityState, (int)state);
    }

    private void LogMessage(string message)
    {
        Log?.Invoke($"[{DateTime.Now:HH:mm:ss}] [传统导航] {message}");
    }

    public async Task CompleteIncompleteDecksAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse = await _client.SendAsync("constructedVisibleDeckCatalog", new { }, 8000, cancellationToken);
        if (!bridgeResponse.Ok)
        {
            LogMessage("读取待补全卡组失败：" + bridgeResponse.Error);
            return;
        }

        foreach (JsonElement item in (
            from d in EnumerateDecks(bridgeResponse.Data)
            where ReadBool(d, "canAutoComplete")select d).Take(100))
        {
            if (!item.TryGetProperty("deckId", out var value) || !value.TryGetInt64(out var deckId) || deckId <= 0)
            {
                continue;
            }

            string name = ReadString(item, "name");
            LogMessage("工作室正在使用已有卡牌自动补全“" + name + "”并保存。");
            bool finished = false;
            string pendingReason = "";
            for (int poll = 0; poll < 25; poll++)
            {
                BridgeResponse bridgeResponse2 = await _client.SendAsync("completeConstructedDeck", new { deckId = deckId.ToString(CultureInfo.InvariantCulture) }, 8000, cancellationToken);
                if (!bridgeResponse2.Ok || !ReadBool(bridgeResponse2.Data, "pending"))
                {
                    LogMessage((bridgeResponse2.Ok && ReadBool(bridgeResponse2.Data, "completed")) ? ("卡组“" + name + "”已补全、保存并通过游戏规则检查。") : ("卡组“" + name + "”未能补全，本次跳过：" + (bridgeResponse2.Ok ? ReadString(bridgeResponse2.Data, "reason") : bridgeResponse2.Error)));
                    finished = true;
                    break;
                }

                string text = ReadString(bridgeResponse2.Data, "reason");
                if (text != pendingReason)
                {
                    LogMessage("卡组“" + name + "”补全状态：" + text);
                    pendingReason = text;
                }

                await Task.Delay(1000, cancellationToken);
            }

            if (!finished)
            {
                LogMessage("卡组“" + name + "”补全等待超时，本次跳过。");
            }
        }
    }
}