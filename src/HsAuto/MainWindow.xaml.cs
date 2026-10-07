using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HsAuto.Open;
using HsAuto.Core.Models;
using HsAuto.Core.Automation;
using HsAuto.Core.Configuration;

namespace HsAuto;

public partial class MainWindow : Window
{
    readonly LocalData data;
    readonly CancellationTokenSource lifetime = new();
    readonly bool smoke;
    CancellationTokenSource? run;
    Task? running;
    Task? activeEngineWork;
    int? activeGamePid;
    Task? installing;
    readonly IRunStopPlatform runStopPlatform;
    TimedStopSession? timedStop;
    bool immediateStopRequested;
    readonly HashSet<Task> foregroundOperations = [];
    bool inspecting;
    bool closing;
    bool closeAllowed;
    IReadOnlyList<DeckEntry> deckCatalog = [];
    bool refreshingDecks;
    Task? profileRefresh;
    bool refreshingProfile;
    string profileSession = "";
    readonly PlayerLogFeed playerLog = new();
    public MainWindow(bool isSmoke = false, IRunStopPlatform? stopPlatform = null)
    {
        smoke = isSmoke;
        // Smoke tests must never fall through to the real process/Windows actions.
        runStopPlatform = stopPlatform ?? (isSmoke ? new SmokeRunStopPlatform() : new WindowsRunStopPlatform());
        data = new LocalData(smoke ? Path.Combine(Path.GetTempPath(), "HsAuto-smoke-" + Guid.NewGuid().ToString("N")) : null);
        InitializeComponent();
        StopRankCombo.ItemsSource = AccountRunStopPolicy.ConstructedRankTargets;
        StopRankCombo.DisplayMemberPath = "DisplayName"; StopRankCombo.SelectedValuePath = "Key";
        ControlRankCombo.ItemsSource = ConstructedRankControlPolicy.Targets;
        ControlRankCombo.DisplayMemberPath = "DisplayName"; ControlRankCombo.SelectedValuePath = "Key";
        SettingsTab("Basic");
        Nav("Account");
        SizeChanged += (_, _) => CompactLayout(ActualHeight);
        Closing += OnClosing;
        if (!smoke)
        {
            Loaded += async (_, _) => await InitializeAsync();
            SourceInitialized += (_, _) => SetBackdrop();
        }
    }
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    void SetBackdrop()
    {
        if (Environment.OSVersion.Version.Build < 22000) return;
        var handle = new WindowInteropHelper(this).Handle; int rounded = 2; DwmSetWindowAttribute(handle, 33, ref rounded, sizeof(int));
        int acrylic = 3;
        if (DwmSetWindowAttribute(handle, 38, ref acrylic, sizeof(int)) == 0)
        {
            Background = Brushes.Transparent;
            System.Windows.Shell.WindowChrome.GetWindowChrome(this).GlassFrameThickness = new Thickness(-1);
        }
        // DWM transparency varies by OS; gradient/material fallback keeps contrast readable.
    }
    async Task InitializeAsync()
    {
        await Guard(async () =>
        {
            await data.LoadAsync();
            UpdateSettings(); RefreshHistory();
            if (!string.IsNullOrWhiteSpace(data.RecoveryMessage)) Log(data.RecoveryMessage);
            await InspectAsync(data.Settings.AutoOpenBox);
        });
        if (!closing) profileRefresh = ProfileRefreshLoopAsync();
    }
    void UpdateSettings()
    {
        ShowIdentity(PlayerIdentityView.Waiting());
        BoxPathBox.Text = data.Settings.BoxPath; GamePathBox.Text = data.Settings.GamePath; DeckBox.Text = data.Settings.DeckName;
        AutoBox.IsChecked = data.Settings.AutoOpenBox; AutoQueue.IsChecked = data.Settings.AutoQueue; ReadOnly.IsChecked = data.Settings.ReadOnly;
        FormatCombo.SelectedIndex = data.Settings.Format == "Wild" ? 1 : data.Settings.Format == "Casual" ? 2 : 0;
        var t = data.Settings.Traditional;
        AutoConcedeBox.IsChecked = t.AutoConcede; WinPatternBox.IsChecked = t.WinThenConcede;
        WinsBox.Text = t.WinsBeforeConcede.ToString(); ConcedesBox.Text = t.ConcedesAfterWins.ToString();
        LowWinRateBox.IsChecked = t.LowWinRateConcede; WinRateThresholdBox.Text = t.WinRateThreshold.ToString();
        RopeProtectionBox.IsChecked = t.RopeProtection; RotateDecksBox.IsChecked = t.RotateDecks;
        RotateEveryBox.Text = t.RotateEvery.ToString(); RotationNamesBox.Text = t.RotationNames;
        StopAtRankBox.IsChecked = t.StopAtRank; StopRankCombo.SelectedValue = t.StopRank;
        ControlRankBox.IsChecked = t.ControlRank; ControlRankCombo.SelectedValue = t.ControlRankTarget;
        LegendStopBox.IsChecked = t.StopAtLegend; LegendThresholdBox.Text = t.LegendThreshold.ToString();
        RestartBoxToggle.IsChecked = t.RestartBox; NoRecommendationBox.Text = t.MissingRecommendationSeconds.ToString();
        RestartCooldownBox.Text = t.RestartCooldownSeconds.ToString();
        var time = data.Settings.Time;
        TimedStopBox.IsChecked = time.Enabled; StopAfterHoursBox.Text = time.Hours.ToString(); StopAfterMinutesBox.Text = time.Minutes.ToString();
        TimedStopActionCombo.SelectedIndex = (int)time.Action;
        UpdateTimedStopReadyText();
        MotionSettings.SetEnabled(this, SystemParameters.ClientAreaAnimation);
    }
    void ReadSettings()
    {
        var boxPath = BoxPathBox.Text.Trim().Trim('"'); var gamePath = GamePathBox.Text.Trim().Trim('"');
        if (boxPath.Length > 0 && !Locator.ValidPath(boxPath, false)) throw new IOException("炉石盒子路径无效，请选择 HSAng.exe。");
        if (gamePath.Length > 0 && !Locator.ValidPath(gamePath, true)) throw new IOException("炉石路径无效，请选择 Hearthstone.exe。");
        var format = ((ComboBoxItem)FormatCombo.SelectedItem).Tag.ToString() ?? "Standard";
        static int Number(TextBox b, string label, int min, int max)
        {
            if (!int.TryParse(b.Text.Trim(), out var number) || number < min || number > max) throw new IOException($"{label}需要输入 {min}～{max} 的整数。");
            return number;
        }
        var traditional = new TraditionalSettings
        {
            AutoConcede = AutoConcedeBox.IsChecked == true, WinThenConcede = WinPatternBox.IsChecked == true,
            WinsBeforeConcede = Number(WinsBox, "赢后投降场数", 1, 999), ConcedesAfterWins = Number(ConcedesBox, "投降场数", 1, 999),
            LowWinRateConcede = LowWinRateBox.IsChecked == true, WinRateThreshold = Number(WinRateThresholdBox, "胜率阈值", 1, 100),
            RopeProtection = RopeProtectionBox.IsChecked == true, RotateDecks = RotateDecksBox.IsChecked == true,
            RotateEvery = Number(RotateEveryBox, "轮换间隔", 1, 999), RotationNames = RotationNamesBox.Text.Trim(),
            StopAtRank = StopAtRankBox.IsChecked == true, StopRank = StopRankCombo.SelectedValue?.ToString() ?? "Diamond5",
            ControlRank = ControlRankBox.IsChecked == true, ControlRankTarget = ControlRankCombo.SelectedValue?.ToString() ?? "Diamond5",
            StopAtLegend = LegendStopBox.IsChecked == true, LegendThreshold = Number(LegendThresholdBox, "传说排名", 1, 1000000),
            RestartBox = RestartBoxToggle.IsChecked == true, MissingRecommendationSeconds = Number(NoRecommendationBox, "无推荐等待秒数", 5, 120),
            RestartCooldownSeconds = Number(RestartCooldownBox, "盒子重启间隔", 30, 3600)
        };
        traditional.Validate(format);
        if (!Enum.TryParse<RunStopAction>((TimedStopActionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var stopAction) || !Enum.IsDefined(stopAction))
            throw new IOException("请选择有效的停止后操作。");
        var time = new RunStopSettings
        {
            Enabled = TimedStopBox.IsChecked == true,
            Hours = Number(StopAfterHoursBox, "定时停止小时", 0, 168),
            Minutes = Number(StopAfterMinutesBox, "定时停止分钟", 0, 59),
            Action = stopAction
        };
        time.Validate();
        if (AutoQueue.IsChecked == true && string.IsNullOrWhiteSpace(DeckBox.Text)) throw new IOException("自动匹配需要填写游戏内卡组名称。");
        // Validate all controls before committing any of the in-memory settings.
        data.Settings.BoxPath = boxPath; data.Settings.GamePath = gamePath;
        data.Settings.DeckName = DeckBox.Text.Trim(); data.Settings.Format = format;
        data.Settings.AutoOpenBox = AutoBox.IsChecked == true; data.Settings.AutoQueue = AutoQueue.IsChecked == true;
        data.Settings.ReadOnly = ReadOnly.IsChecked == true;
        data.Settings.ReduceMotion = false; // Old settings migrate; motion now follows Windows accessibility.
        data.Settings.Traditional = traditional;
        data.Settings.Time = time;
        UpdateTimedStopReadyText();
        MotionSettings.SetEnabled(this, SystemParameters.ClientAreaAnimation);
    }
    async Task<FoundApp> InspectAsync(bool launchBox, Func<bool, string, FoundApp>? discovery = null, bool inspectBridge = true)
    {
        if (inspecting) throw new InvalidOperationException("检测正在进行，请稍候。");
        inspecting = true; InspectButton.IsEnabled = false;
        try
        {
            var requestedBox = Locator.NormalizePath(BoxPathBox.Text);
            var requestedGame = Locator.NormalizePath(GamePathBox.Text);
            var savedBox = Locator.ValidPath(requestedBox, false) ? requestedBox : data.Settings.BoxPath;
            var savedGame = Locator.ValidPath(requestedGame, true) ? requestedGame : data.Settings.GamePath;
            discovery ??= Locator.Find;
            var box = await Task.Run(() => discovery(false, savedBox), lifetime.Token);
            var game = await Task.Run(() => discovery(true, savedGame), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            BoxStatus.Text = box.Found ? box.Pid.HasValue ? "运行中" : "已找到" : "需要安装";
            GameStatus.Text = game.Source.StartsWith("多个") ? "需选择目标" : game.Pid.HasValue ? "运行中" : game.Found ? "等待战网启动" : "需要定位";
            if (box.Found) { data.Settings.BoxPath = box.Path; BoxPathBox.Text = box.Path; BoxStatus.ToolTip = box.Path; }
            else { Log("没有自动识别到炉石盒子。请重新安装炉石传说盒子，然后点击重新检测；也可在引擎设置手动定位。"); StatusNote.Text = "请重新安装炉石盒子后重试。"; }
            if (game.Found) { data.Settings.GamePath = game.Path; GamePathBox.Text = game.Path; GameStatus.ToolTip = game.Path; }
            // Keep the visible fields in sync with the normalized values even when
            // discovery completed before the settings page was opened.
            BoxPathBox.Text = data.Settings.BoxPath;
            GamePathBox.Text = data.Settings.GamePath;
            // Persist detection immediately. A later bridge/deck failure must not lose discovered paths.
            await data.SaveAsync();
            if (box.Found && game.Found) Log("已识别并保存炉石盒子和炉石路径；安装与连接已回填。");
            if (launchBox && box.Found && !box.Pid.HasValue)
            {
                Locator.Open(box); Log("已打开炉石盒子。请正常启动炉石。"); BoxStatus.Text = "已请求启动";
            }
            if (inspectBridge && game.Pid.HasValue)
            {
                var ping = await new UnityBridgeClient("HsAuto.OpenBridge." + game.Pid).SendAsync("ping", timeoutMs: 1500, cancellationToken: lifetime.Token);
                bool compatible = BridgeHandshake.Matches(ping, game.Pid.Value);
                BridgeStatus.Text = compatible ? "已连接" : "待安装 / 重启";
                if (compatible)
                {
                    var account = await new UnityBridgeClient("HsAuto.OpenBridge." + game.Pid).SendAsync("accountDetails", timeoutMs: 10000, cancellationToken: lifetime.Token);
                    if (account.Ok) ShowAccount(account.Data);
                    await RefreshDecksAsync(game.Pid.Value);
                }
            }
            else BridgeStatus.Text = game.Source.StartsWith("多个") ? "请确认目标客户端" : "等待游戏启动";
            return game;
        }
        finally { inspecting = false; InspectButton.IsEnabled = true; }
    }
    async Task Guard(Func<Task> operation)
    {
        if (closing) return;
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foregroundOperations.Add(finished.Task);
        try { await operation(); }
        catch (OperationCanceledException) { if (!closing) Log("操作已取消。"); }
        catch (Exception ex) { Log("[错误] " + ex.Message); StatusNote.Text = "查看下方日志定位原因。"; }
        finally { foregroundOperations.Remove(finished.Task); finished.TrySetResult(); }
    }
    void Log(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Log(text)); return; }
        if (closing) return;
        text = UiText.WithoutTimestamp(text);
        var now = DateTimeOffset.Now;
        var diagnosticLine = $"[{now:HH:mm:ss}] {text}";
        try { Directory.CreateDirectory(data.DirectoryPath); File.AppendAllText(Path.Combine(data.DirectoryPath, "controller-0.1.8.log"), diagnosticLine + Environment.NewLine); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A log write failure must not stop the UI. */ }
        if (text.StartsWith("UnityBridge 协议 1 已连接")) BridgeStatus.Text = "已连接";
        if (timedStop is { Expiring: true } && text.StartsWith("数据已保存，30秒后请求关机"))
        {
            TimedStopCountdown.Text = "30秒后请求关机";
            TimedStopHint.Text = "可点击“取消本次定时操作”或“停止”取消关机。";
        }
        var display = playerLog.Accept(text, now);
        if (display == null) return;
        var line = $"[{now:HH:mm:ss}] {display}";
        if (LogBox.Text.Length > 50000) LogBox.Text = LogBox.Text[^30000..];
        LogBox.AppendText(line + Environment.NewLine); LogBox.ScrollToEnd();
        if (text.StartsWith("[推荐]") || text.StartsWith("[只读]")) Recommendation.Text = display;
    }
    void ShowAccount(JsonElement account)
    {
        var pieces = new List<string>();
        void Add(string property, string label) { if (account.TryGetProperty(property, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) pieces.Add(label + " " + value.ToString()); }
        ShowIdentity(PlayerIdentityView.Parse(account));
        Add("gold", "金币"); Add("dust", "奥术之尘"); Add("arenaTickets", "门票"); Add("battlegroundsRating", "战棋评分");
        AccountSummary.Text = pieces.Count > 0 ? UiText.Normalize(string.Join(" · ", pieces)) : "等待账号概览。";
        if (account.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.False) AccountSummary.Text += "（账号数据尚未全部就绪）";
    }
    void ShowIdentity(PlayerIdentityView identity)
    {
        AccountName.Text = identity.Name;
        IdentityHint.Text = identity.Hint;
        AccountName.ToolTip = identity.Name;
    }
    static string SessionStamp(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.HasExited ? "" : pid + ":" + p.StartTime.ToUniversalTime().Ticks; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return ""; }
    }
    async Task RefreshProfileAsync()
    {
        if (closing || refreshingProfile || inspecting || installing is { IsCompleted: false }) return;
        refreshingProfile = true;
        try
        {
            var savedPath = data.Settings.GamePath;
            var game = await Task.Run(() => Locator.Find(true, savedPath), lifetime.Token);
            if (!game.Pid.HasValue)
            {
                profileSession = "";
                ShowIdentity(PlayerIdentityView.Waiting(game.Source.StartsWith("多个") ? "检测到多个炉石实例，请先确认目标客户端。" : "等待炉石启动并登录；无需手动填写玩家名称。"));
                return;
            }
            var stamp = SessionStamp(game.Pid.Value);
            if (stamp.Length == 0) { profileSession = ""; ShowIdentity(PlayerIdentityView.Waiting()); return; }
            if (profileSession != stamp)
            {
                profileSession = stamp; ShowIdentity(PlayerIdentityView.Waiting());
                AccountSummary.Text = "等待当前客户端账号概览。";
            }
            var client = new UnityBridgeClient("HsAuto.OpenBridge." + game.Pid.Value);
            var ping = await client.SendAsync("ping", timeoutMs: 1500, cancellationToken: lifetime.Token);
            if (!BridgeHandshake.Matches(ping, game.Pid.Value))
            { ShowIdentity(PlayerIdentityView.Waiting("玩家名称识别需要插件连接：退出炉石后安装/修复插件，再正常启动游戏。")); return; }
            var response = await client.SendAsync("playerIdentity", timeoutMs: 2000, cancellationToken: lifetime.Token);
            if (closing || savedPath != data.Settings.GamePath || SessionStamp(game.Pid.Value) != stamp) return;
            ShowIdentity(response.Ok ? PlayerIdentityView.Parse(response.Data) : PlayerIdentityView.Waiting("旧插件未支持玩家昵称识别；请安装/修复插件后重启炉石。"));
        }
        finally { refreshingProfile = false; }
    }
    async Task ProfileRefreshLoopAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try { await RefreshProfileAsync(); await Task.Delay(5000, lifetime.Token); }
            catch (OperationCanceledException) { return; }
            catch (Exception)
            {
                if (closing) return;
                ShowIdentity(PlayerIdentityView.Waiting("玩家名称读取暂不可用，稍后自动重试。"));
                try { await Task.Delay(5000, lifetime.Token); } catch (OperationCanceledException) { return; }
            }
        }
    }
    void ShowState(ConstructedGameState state)
    {
        MatchPhase.Text = state.Phase switch { ConstructedPhase.Mulligan => "起手换牌", ConstructedPhase.LocalTurn => "我方回合", ConstructedPhase.OpponentTurn => "对手回合", ConstructedPhase.Discover => "发现选择", ConstructedPhase.Choice => "等待选择", ConstructedPhase.GameOver => "对局结束", ConstructedPhase.Queue => "匹配 / 大厅", _ => "等待就绪" };
        MatchInfo.Text = $"第 {state.Turn} 回合 · 法力 {state.ManaAvailable}/{state.ManaTotal}\n手牌 {state.Hand.Count} · 我方生命 {state.HeroHealth} · 对手生命 {state.OpponentHealth}";
    }
    async void StartClick(object sender, RoutedEventArgs e)
    {
        if (closing || running is { IsCompleted: false } || installing is { IsCompleted: false } || timedStop != null) return;
        StartButton.IsEnabled = false; StopButton.IsEnabled = true;
        run = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        ImmediateStopButton.IsEnabled = true;
        running = StartRunAsync(run); await running;
    }
    async Task StartRunAsync(CancellationTokenSource source)
    {
        var ct = source.Token;
        Task? engineWork = null;
        try
        {
            ReadSettings(); await data.SaveAsync();
            var timeSettings = data.Settings.Time.Snapshot();
            RunStatus.Text = "检查插件"; var game = await InspectAsync(true, inspectBridge: false);
            if (game.Source.StartsWith("多个", StringComparison.Ordinal))
                throw new IOException("检测到多个炉石实例，无法安全确定目标账号。请保留要使用的客户端后重新检测；不会按枚举顺序选择或强制改绑。");
            if (!Locator.Find(false, data.Settings.BoxPath).Found) throw new IOException("未找到炉石盒子，请重新安装后重试。");
            if (!game.Found) throw new IOException("请先在安装与连接中定位炉石传说，然后开始脚本。");
            string installGamePath = game.Path;
            await PluginStartup.EnsureAsync(
                () => BridgeInstaller.IsCurrentInstallation(installGamePath, AppContext.BaseDirectory),
                BridgeInstaller.GameIsRunning,
                async () =>
                {
                    var task = Task.Run(() => BridgeInstaller.Install(installGamePath, AppContext.BaseDirectory, data.DirectoryPath));
                    installing = task; Log(await task);
                }, Log, ct);
            RunStatus.Text = "等待游戏";
            game = await Task.Run(() => Locator.Find(true, installGamePath), ct);
            if (!game.Pid.HasValue) Log("请正常启动并登录炉石，脚本保持等待，可随时停止。");
            while (!game.Pid.HasValue)
            {
                ct.ThrowIfCancellationRequested();
                if (game.Source.StartsWith("多个", StringComparison.Ordinal))
                    Log("检测到多个炉石实例，请只保留要连接的客户端；脚本保持等待。");
                await Task.Delay(1000, ct);
                game = await Task.Run(() => Locator.Find(true, installGamePath), ct);
            }
            ct.ThrowIfCancellationRequested();
            if (game.Found) { data.Settings.GamePath = game.Path; GamePathBox.Text = game.Path; await data.SaveAsync(); }
            activeGamePid = game.Pid.Value;
            var engines = new Automation(); engines.Log += Log;
            var stopTargets = timeSettings.Enabled ? CaptureTimedStopTargets(game.Pid.Value, data.Settings.GamePath, data.Settings.BoxPath, timeSettings.Action) : (Game: (IReadOnlyList<StopProgramIdentity>)[], Clients: (IReadOnlyList<StopProgramIdentity>)[]);
            bool timerArmed = false;
            engines.State += s => Dispatcher.BeginInvoke(() =>
            {
                if (closing || source.IsCancellationRequested || !ReferenceEquals(run, source)) return;
                ShowState(s);
                // Start only after the engine has actually reported a readable
                // state, not while discovery or connection is still pending.
                if (!timerArmed)
                {
                    timerArmed = true;
                    BeginTimedStop(timeSettings, source, () => engineWork ?? Task.CompletedTask, stopTargets.Game, stopTargets.Clients);
                }
            });
            engines.Account += a => Dispatcher.BeginInvoke(() => ShowAccount(a));
            engines.MatchesChanged += () => Dispatcher.BeginInvoke(RefreshHistory);
            GameStatus.Text = "运行中"; BridgeStatus.Text = "正在连接"; RunStatus.Text = data.Settings.ReadOnly ? "只读观察" : "运行中";
            Log("正在连接炉石 PID=" + game.Pid.Value + "，初始化真实推荐源与 Bridge…");
            engineWork = Task.Run(() => engines.RunAsync(game.Pid.Value, data, ct), ct);
            activeEngineWork = engineWork;
            await engineWork;
            BridgeStatus.Text = "已连接";
        }
        catch (OperationCanceledException) { if (!closing) Log(timedStop is { Expiring: true } ? "脚本已停止，正在完成定时操作。" : "脚本已停止，盒子和炉石保持打开。"); }
        catch (Exception ex) { Log("[停止] " + ex.Message); BridgeStatus.Text = "查看日志"; }
        finally
        {
            if (timedStop is { Expiring: false } timer && ReferenceEquals(timer.RunSource, source)) CancelTimedStop(false);
            if (ReferenceEquals(run, source)) { run = null; activeEngineWork = null; activeGamePid = null; }
            source.Dispose();
            if (!closing)
            {
                var completing = timedStop is { Expiring: true };
                RunStatus.Text = completing ? "定时操作中" : "已停止";
                StartButton.IsEnabled = timedStop == null; StopButton.IsEnabled = completing;
                ImmediateStopButton.IsEnabled = false;
            }
        }
    }
    void StopClick(object sender, RoutedEventArgs e)
    {
        CancelTimedStop(true);
        run?.Cancel(); RunStatus.Text = run != null ? "停止中" : "已停止";
    }
    static string StopActionLabel(RunStopAction action) => action switch
    {
        RunStopAction.ExitGame => "退出炉石",
        RunStopAction.ExitGameAndClients => "退出炉石、战网和炉石盒子",
        RunStopAction.Shutdown => "停止后关机",
        _ => "仅停止脚本"
    };
    void UpdateTimedStopReadyText()
    {
        if (timedStop != null) return;
        TimedStopCountdown.Text = data.Settings.Time.Enabled ? $"待开始 · {data.Settings.Time.Hours} 小时 {data.Settings.Time.Minutes} 分钟" : "未启用定时停止";
        TimedStopHint.Text = "连接成功并开始运行后计时，保存设置不会开始倒计时。";
        CancelTimedStopButton.IsEnabled = false;
        ImmediateStopButton.IsEnabled = !immediateStopRequested && run is { IsCancellationRequested: false };
    }
    (IReadOnlyList<StopProgramIdentity> Game, IReadOnlyList<StopProgramIdentity> Clients) CaptureTimedStopTargets(int pid, string gamePath, string boxPath, RunStopAction action)
    {
        if (action == RunStopAction.StopOnly) return ([], []);
        if (smoke) return ([new(42420, Path.Combine(data.DirectoryPath, "Hearthstone.exe"), DateTimeOffset.UnixEpoch)], []);
        var game = WindowsRunStopPlatform.Capture("Hearthstone", gamePath, pid, requireSafe: true);
        if (game.Count != 1) throw new IOException("本次炉石进程无法安全确认，未启用退出或关机操作；请重新连接游戏。");
        if (action == RunStopAction.ExitGame) return (game, []);
        var clients = new List<StopProgramIdentity>();
        if (!string.IsNullOrWhiteSpace(boxPath)) clients.AddRange(WindowsRunStopPlatform.Capture(Path.GetFileNameWithoutExtension(boxPath), boxPath, requireSafe: true));
        clients.AddRange(WindowsRunStopPlatform.Capture("Battle.net", requireSafe: true));
        clients.AddRange(WindowsRunStopPlatform.Capture("Battle.net Launcher", requireSafe: true));
        return (game, clients);
    }
    void BeginTimedStop(RunStopSettings settings, CancellationTokenSource source, Func<Task> drain,
        IReadOnlyList<StopProgramIdentity> game, IReadOnlyList<StopProgramIdentity> clients, RunStopCountdown? countdown = null,
        bool immediate = false, Func<Task>? save = null, TimeSpan? drainTimeout = null)
    {
        if (closing || (!settings.Enabled && !immediate) || source.IsCancellationRequested) { UpdateTimedStopReadyText(); return; }
        if (immediate) { if (!Enum.IsDefined(settings.Action)) throw new IOException("请选择有效的停止后操作。"); }
        else settings.Validate();
        if (timedStop != null) throw new InvalidOperationException("请等待上一次定时操作完成后再开始。");
        var session = new TimedStopSession(settings.Snapshot(), source, drain, game.ToArray(), clients.ToArray(),
            countdown ?? new RunStopCountdown(immediate ? TimeSpan.FromTicks(1) : settings.Duration),
            CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token), immediate, save ?? data.SaveAsync, drainTimeout ?? TimeSpan.FromSeconds(15));
        timedStop = session;
        CancelTimedStopButton.IsEnabled = true;
        ImmediateStopButton.IsEnabled = !immediate && !immediateStopRequested;
        TimedStopHint.Text = $"到时：{StopActionLabel(settings.Action)}。可随时取消本次定时操作。";
        Log(immediate ? $"现在停止并{StopActionLabel(settings.Action)}。" : $"已启用定时停止：{settings.Hours} 小时 {settings.Minutes} 分钟后{StopActionLabel(settings.Action)}。");
        session.MonitorTask = MonitorTimedStopAsync(session);
    }
    async Task MonitorTimedStopAsync(TimedStopSession session)
    {
        // Ensure MonitorTask is assigned even when an injected test clock has
        // already reached its deadline. All UI continuations stay on Dispatcher.
        await Task.Yield();
        var token = session.Cancel.Token;
        try
        {
            while (!session.Immediate)
            {
                token.ThrowIfCancellationRequested();
                if (session.Countdown.TryExpire()) break;
                var left = session.Countdown.Remaining;
                TimedStopCountdown.Text = $"剩余 {(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
                await Task.Delay(250, token);
            }
            session.Expiring = true; StartButton.IsEnabled = false; StopButton.IsEnabled = true;
            ImmediateStopButton.IsEnabled = false;
            RunStatus.Text = "停止操作中"; TimedStopCountdown.Text = session.Immediate ? "正在立即停止" : "已到时间，正在安全停止";
            TimedStopHint.Text = "先停止运行并保存设置与战绩；取消可阻止尚未执行的退出和关机。";
            Log(session.Immediate ? "正在停止脚本并保存数据。" : "已到设定时间，正在停止脚本并保存数据。");
            await RunStopSequence.ExecuteAsync(session.Settings.Action,
                async () =>
                {
                    session.RunSource.Cancel();
                    // Await only the engine worker, never StartRunAsync/this
                    // monitor: neither can wait on itself during finalization.
                    var worker = session.Drain();
                    try { await worker.WaitAsync(session.DrainTimeout, token); }
                    catch (OperationCanceledException) when (worker.IsCanceled && !token.IsCancellationRequested)
                    { /* Expected: the worker observed our stop request and has drained. */ }
                }, session.Save, session.Game, session.Clients, runStopPlatform, Log, token);
            token.ThrowIfCancellationRequested();
            TimedStopCountdown.Text = "本次定时操作已完成";
            TimedStopHint.Text = "时间设置已保留；下次开始运行会重新计时。";
            Log("定时操作已完成。" );
        }
        catch (OperationCanceledException)
        {
            if (!closing) { TimedStopCountdown.Text = "本次定时已取消"; TimedStopHint.Text = "已取消尚未执行的操作；已保存的时间设置不变。"; }
        }
        catch (Exception ex)
        {
            if (!closing) { TimedStopCountdown.Text = "定时操作未完成"; TimedStopHint.Text = "不会继续执行退出或关机，请查看日志。"; Log("定时操作停止：" + ex.Message); }
        }
        finally
        {
            if (ReferenceEquals(timedStop, session))
            {
                timedStop = null;
                CancelTimedStopButton.IsEnabled = false;
                ImmediateStopButton.IsEnabled = !closing && !immediateStopRequested && run is { IsCancellationRequested: false };
                if (!closing && run == null) { RunStatus.Text = "已停止"; StartButton.IsEnabled = true; StopButton.IsEnabled = false; }
            }
            session.Cancel.Dispose();
        }
    }
    void CancelTimedStop(bool report)
    {
        if (timedStop is not { } session) return;
        session.Countdown.Cancel(); session.Cancel.Cancel();
        CancelTimedStopButton.IsEnabled = false;
        if (report && !closing) Log("已取消本次定时操作，未执行的退出和关机不会继续。");
    }
    void CancelTimedStopClick(object sender, RoutedEventArgs e) => CancelTimedStop(true);
    async void ImmediateStopClick(object sender, RoutedEventArgs e) => await Guard(ImmediateStopAsync);
    async Task ImmediateStopAsync()
    {
        if (closing || immediateStopRequested || run is not { IsCancellationRequested: false } source) return;
        immediateStopRequested = true; ImmediateStopButton.IsEnabled = false;
        try
        {
            if (!Enum.TryParse<RunStopAction>((TimedStopActionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var action) || !Enum.IsDefined(action))
                throw new IOException("请选择有效的停止后操作。");
            if (!smoke && action != RunStopAction.StopOnly && MessageBox.Show(this,
                    $"立即停止脚本并{StopActionLabel(action)}？\n会先保存设置与战绩，不强制结束其他程序。",
                    "HearthLeap", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (closing || !ReferenceEquals(run, source) || source.IsCancellationRequested) return;
            var targets = action == RunStopAction.StopOnly ? (Game: (IReadOnlyList<StopProgramIdentity>)[], Clients: (IReadOnlyList<StopProgramIdentity>)[])
                : CaptureTimedStopTargets(activeGamePid ?? throw new IOException("请等待本次游戏连接完成后再执行退出或关机。"), data.Settings.GamePath, data.Settings.BoxPath, action);
            var drain = activeEngineWork ?? running ?? Task.CompletedTask;
            var old = timedStop;
            CancelTimedStop(false);
            if (old != null) await old.MonitorTask;
            if (closing || !ReferenceEquals(run, source) || source.IsCancellationRequested) return;
            BeginTimedStop(new() { Action = action }, source, () => drain, targets.Game, targets.Clients, immediate: true);
            if (timedStop is { } session) await session.MonitorTask;
        }
        finally
        {
            immediateStopRequested = false;
            ImmediateStopButton.IsEnabled = !closing && timedStop is not { Expiring: true } && run is { IsCancellationRequested: false };
        }
    }
    sealed class TimedStopSession(RunStopSettings settings, CancellationTokenSource runSource, Func<Task> drain,
        IReadOnlyList<StopProgramIdentity> game, IReadOnlyList<StopProgramIdentity> clients, RunStopCountdown countdown, CancellationTokenSource cancel,
        bool immediate, Func<Task> save, TimeSpan drainTimeout)
    {
        public RunStopSettings Settings { get; } = settings;
        public CancellationTokenSource RunSource { get; } = runSource;
        public Func<Task> Drain { get; } = drain;
        public IReadOnlyList<StopProgramIdentity> Game { get; } = game;
        public IReadOnlyList<StopProgramIdentity> Clients { get; } = clients;
        public RunStopCountdown Countdown { get; } = countdown;
        public CancellationTokenSource Cancel { get; } = cancel;
        public bool Immediate { get; } = immediate;
        public Func<Task> Save { get; } = save;
        public TimeSpan DrainTimeout { get; } = drainTimeout;
        public bool Expiring { get; set; }
        public Task MonitorTask { get; set; } = Task.CompletedTask;
    }
    sealed class SmokeRunStopPlatform : IRunStopPlatform
    {
        public List<string> Events { get; } = [];
        public TaskCompletionSource? ShutdownGate { get; set; }
        public TaskCompletionSource? ShutdownEntered { get; set; }
        public Task CloseAsync(IReadOnlyList<StopProgramIdentity> programs, Action<string> log, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Events.Add("close:" + programs.Count); return Task.CompletedTask; }
        public async Task ScheduleShutdownAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); ShutdownEntered?.TrySetResult();
            if (ShutdownGate != null) await ShutdownGate.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested(); Events.Add("shutdown");
        }
        public Task CancelShutdownAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Events.Add("cancel-shutdown"); return Task.CompletedTask; }
    }
    async void InspectClick(object sender, RoutedEventArgs e) => await Guard(async () => await InspectAsync(false));
    async void SaveSettingsClick(object sender, RoutedEventArgs e) => await Guard(async () => { ReadSettings(); await data.SaveAsync(); Log("引擎设置已保存，下次启动脚本生效。"); });
    string SelectedFormat => (FormatCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Standard";
    void FormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeckBox != null && DeckHint != null) ApplyDeckCatalog();
    }
    void ApplyDeckCatalog()
    {
        var input = DeckBox.Text;
        var names = DeckCatalog.Names(deckCatalog, SelectedFormat);
        DeckBox.ItemsSource = names; DeckBox.Text = input;
        DeckHint.Text = names.Count > 0 ? $"识别到 {names.Count} 副当前模式完整卡组；保留手动输入。" : "未识别到当前模式完整卡组，可手动输入；请在游戏大厅刷新。";
    }
    async Task RefreshDecksAsync(int pid)
    {
        if (refreshingDecks) return;
        refreshingDecks = true; DeckRefreshButton.IsEnabled = false;
        try
        {
            var response = await new UnityBridgeClient("HsAuto.OpenBridge." + pid).SendAsync(DeckCatalog.Command, timeoutMs: 8000, cancellationToken: lifetime.Token);
            if (!response.Ok) { DeckHint.Text = "游戏卡组尚未就绪，保留已有列表和输入；回到大厅后刷新。"; Log("[卡组] " + response.Error); return; }
            var parsed = DeckCatalog.Parse(response.Data);
            if (parsed.Count == 0) { DeckHint.Text = "暂未读取到卡组，保留已有列表和输入；回到大厅后刷新。"; return; }
            deckCatalog = parsed; ApplyDeckCatalog();
        }
        finally { refreshingDecks = false; DeckRefreshButton.IsEnabled = true; }
    }
    // Snapshot WPF values before scheduling any background work. The injectable
    // discovery delegate also enables the real handler path to be tested offline.
    async Task<FoundApp> FindGameForDeckRefreshAsync(Func<bool, string, FoundApp>? discovery = null)
    {
        Dispatcher.VerifyAccess();
        var requestedGamePath = GamePathBox.Text.Trim();
        discovery ??= Locator.Find;
        return await Task.Run(() => discovery(true, requestedGamePath), lifetime.Token);
    }
    async void RefreshDecksClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        // Read WPF controls on the dispatcher before entering the worker thread.
        // The previous code accessed GamePathBox.Text inside Task.Run and caused
        // the recurring “另一个线程拥有该对象” refresh failure.
        var game = await FindGameForDeckRefreshAsync();
        if (!game.Pid.HasValue) throw new IOException("请先通过战网打开炉石，插件就绪后才能读取卡组。");
        var ping = await new UnityBridgeClient("HsAuto.OpenBridge." + game.Pid.Value).SendAsync("ping", timeoutMs: 1500, cancellationToken: lifetime.Token);
        if (!BridgeHandshake.Matches(ping, game.Pid.Value)) throw new IOException("请先安装/修复插件并重启炉石。");
        await RefreshDecksAsync(game.Pid.Value);
    });
    async void InstallClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (installing is { IsCompleted: false } || running is { IsCompleted: false }) throw new InvalidOperationException("请等待当前任务结束或先停止脚本，再安装桥接。");
        ReadSettings();
        if (MessageBox.Show("将安装/修复开源插件，并备份旧 HsAuto 插件。不会修改 Hearthstone.exe。\n请先退出炉石。是否继续？", "安装/修复插件", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        RunStatus.Text = "安装中";
        var button = (Button)sender; button.IsEnabled = false;
        try
        {
            var task = Task.Run(() => BridgeInstaller.Install(data.Settings.GamePath, AppContext.BaseDirectory, data.DirectoryPath));
            installing = task;
            Log(await task);
        }
        finally { RunStatus.Text = "准备就绪"; button.IsEnabled = true; }
    });
    void BrowseBoxClick(object sender, RoutedEventArgs e) => Browse(false);
    async void DeletePluginClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        if (installing is { IsCompleted: false } || running is { IsCompleted: false }) throw new InvalidOperationException("请先停止脚本或等待安装结束。");
        var path = GamePathBox.Text.Trim().Trim('"');
        if (!Locator.ValidPath(path, true)) throw new IOException("请先定位 Hearthstone.exe。");
        if (MessageBox.Show("将备份并删除 HearthLeap 插件。\n保留 BepInEx、其他插件和历史备份，不自动恢复旧 hsmm。\n请先退出炉石。是否继续？", "删除插件", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        var button = (Button)sender; button.IsEnabled = false;
        RunStatus.Text = "删除中";
        try { var task = Task.Run(() => BridgeInstaller.Uninstall(path, data.DirectoryPath)); installing = task; Log(await task); BridgeStatus.Text = "未安装"; }
        finally { RunStatus.Text = "准备就绪"; button.IsEnabled = true; }
    });
    void BrowseGameClick(object sender, RoutedEventArgs e) => Browse(true);
    void Browse(bool game)
    {
        var picker = new OpenFileDialog { Filter = game ? "炉石主程序|Hearthstone.exe" : "炉石盒子|HSAng.exe;HSBox.exe;HearthstoneBox.exe", Title = game ? "选择 Hearthstone.exe" : "选择炉石盒子主程序" };
        if (picker.ShowDialog(this) == true) { if (game) GamePathBox.Text = picker.FileName; else BoxPathBox.Text = picker.FileName; }
    }
    void RefreshHistory()
    {
        var wins = data.Matches.Count(m => m.Result == "Win"); var losses = data.Matches.Count(m => m.Result == "Loss");
        MatchCount.Text = data.Matches.Count.ToString(); WinLoss.Text = $"{wins} / {losses}"; WinRate.Text = wins + losses == 0 ? "—" : $"{100.0 * wins / (wins + losses):0.0}%";
        EmptyHistory.Visibility = data.Matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        History.ItemsSource = data.Matches.OrderByDescending(m => m.FinishedAt).Take(100).Select(m => $"{m.FinishedAt:MM-dd HH:mm}    {m.Format}    {(m.Result == "Win" ? "胜利" : m.Result == "Loss" ? "失败" : "平局")}    {(m.FinishedAt - m.StartedAt).TotalMinutes:0.0} 分钟").ToArray();
    }
    async void ExportClick(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var picker = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "HearthLeap-战绩.csv" };
        if (picker.ShowDialog(this) != true) return;
        static string Quote(string s) => "\"" + (s.StartsWith('=') || s.StartsWith('+') || s.StartsWith('-') || s.StartsWith('@') ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        var rows = data.Matches.Select(m => string.Join(",", new[] { m.Id, m.StartedAt.ToString("O"), m.FinishedAt.ToString("O"), m.Format, m.Result }.Select(Quote)));
        await File.WriteAllLinesAsync(picker.FileName, new[] { "MatchId,StartedAt,FinishedAt,Format,Result" }.Concat(rows), new UTF8Encoding(true)); Log("战绩已导出。");
    });
    void OpenLogsClick(object sender, RoutedEventArgs e) { Directory.CreateDirectory(data.DirectoryPath); using var p = Process.Start(new ProcessStartInfo(data.DirectoryPath) { UseShellExecute = true }); }
    void NavClick(object sender, RoutedEventArgs e) => Nav(((Button)sender).Tag.ToString() ?? "Account");
    void SettingsTabClick(object sender, RoutedEventArgs e) => SettingsTab(((Button)sender).Tag.ToString() ?? "Basic");
    void SettingsTab(string page)
    {
        BasicSettingsPanel.Visibility = page == "Basic" ? Visibility.Visible : Visibility.Collapsed;
        TraditionalPanel.Visibility = page == "Traditional" ? Visibility.Visible : Visibility.Collapsed;
        RankPanel.Visibility = page == "Rank" ? Visibility.Visible : Visibility.Collapsed;
        TimePanel.Visibility = page == "Time" ? Visibility.Visible : Visibility.Collapsed;
        BasicSettingsNav.Background = new SolidColorBrush(page == "Basic" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
        TraditionalSettingsNav.Background = new SolidColorBrush(page == "Traditional" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
        RankSettingsNav.Background = new SolidColorBrush(page == "Rank" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
        TimeSettingsNav.Background = new SolidColorBrush(page == "Time" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
    }
    void Nav(string page)
    {
        AccountPage.Visibility = page == "Account" ? Visibility.Visible : Visibility.Collapsed; StatsPage.Visibility = page == "Stats" ? Visibility.Visible : Visibility.Collapsed; SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        var engine = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        EngineActionPanel.Visibility = engine; EngineStatusPanel.Visibility = engine; EngineLogPanel.Visibility = engine;
        PageTitle.Text = page == "Stats" ? "对战分析" : page == "Settings" ? "引擎设置" : "玩家档案";
        PageSubtitle.Text = page == "Stats" ? "真实记录每一局，让表现一目了然。" : page == "Settings" ? "一次设置，下次打开更简单。" : "连接你的游戏，保持操作简单。";
        AccountNav.Background = new SolidColorBrush(page == "Account" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
        StatsNav.Background = new SolidColorBrush(page == "Stats" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
        SettingsNav.Background = new SolidColorBrush(page == "Settings" ? Color.FromArgb(190, 214, 231, 255) : Color.FromArgb(90, 255, 255, 255));
        if (!smoke && SystemParameters.ClientAreaAnimation) { PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(180))); }
        CompactLayout(ActualHeight > 0 ? ActualHeight : Height);
    }
    void CompactLayout(double height)
    {
        // Give common settings priority at the minimum window height; keep a live log line visible.
        LogRow.Height = new GridLength(SettingsPage.Visibility != Visibility.Visible ? 0 : height < 750 ? 60 : 110);
    }
    void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void CloseClick(object sender, RoutedEventArgs e) => Close();
    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; IsEnabled = false;
        CancelTimedStop(false); run?.Cancel(); lifetime.Cancel();
        try
        {
            var pending = foregroundOperations.Concat(running == null ? [] : new[] { running }).Concat(profileRefresh == null ? [] : new[] { profileRefresh })
                .Concat(timedStop == null ? [] : new[] { timedStop.MonitorTask }).ToArray();
            await ShutdownDrain.DrainAsync(pending, installing, data.SaveAsync, TimeSpan.FromSeconds(15), message =>
            {
                try { Directory.CreateDirectory(data.DirectoryPath); File.AppendAllText(Path.Combine(data.DirectoryPath, "errors-0.1.8.log"), DateTime.Now.ToString("O") + " " + UiText.Normalize(message) + Environment.NewLine); }
                catch (Exception) { /* Closing must not be blocked by a read-only log directory. */ }
            });
        }
        finally { closeAllowed = true; Close(); }
    }
    public async Task SmokeAsync(string? folder)
    {
        try
        {
            folder ??= Path.Combine(AppContext.BaseDirectory, "smoke"); Directory.CreateDirectory(folder);
            await data.LoadAsync(); UpdateSettings(); RefreshHistory();
            Width = 1160; Height = 810;
            var visual = (FrameworkElement)Content;
            visual.Measure(new Size(1160, 810)); visual.Arrange(new Rect(0, 0, 1160, 810)); visual.UpdateLayout();
            foreach (var page in new[] { "Account", "Stats", "Settings" })
            {
                data.Settings.ReduceMotion = true; Nav(page); visual.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1160, 810, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var pixels = new byte[1160 * 810 * 4]; bitmap.CopyPixels(pixels, 1160 * 4, 0);
                if (pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0) < 600000) throw new IOException("UI render is blank.");
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, page + ".png")); png.Save(file);
            }
            void Snapshot(string name, int width, int height, double scale = 1)
            {
                var bitmap = new RenderTargetBitmap((int)Math.Round(width * scale), (int)Math.Round(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(file);
            }
            SettingsTab("Traditional"); visual.UpdateLayout(); Snapshot("Traditional", 1160, 810);
            SettingsTab("Rank"); visual.UpdateLayout(); Snapshot("Rank", 1160, 810);
            SettingsTab("Time"); visual.UpdateLayout(); Snapshot("Time", 1160, 810);
            if (TimePanel.Visibility != Visibility.Visible || BasicSettingsPanel.Visibility != Visibility.Collapsed ||
                RankPanel.Visibility != Visibility.Collapsed || TraditionalPanel.Visibility != Visibility.Collapsed)
                throw new IOException("Time settings page did not become the exclusive active page.");
            SettingsTab("Basic");
            foreach (var size in new[] { new Size(1160, 810), new Size(960, 700) })
            {
                Width = size.Width; Height = size.Height; CompactLayout(size.Height);
                visual.Measure(size); visual.Arrange(new Rect(new Point(), size)); visual.UpdateLayout();
                Snapshot("Settings-" + size.Width + "x" + size.Height, (int)size.Width, (int)size.Height);
                var navigation = new[] { AccountNav, StatsNav, SettingsNav };
                if (navigation.Any(b => b.HorizontalContentAlignment != HorizontalAlignment.Center) || navigation.Max(b => b.ActualWidth) - navigation.Min(b => b.ActualWidth) > 2) throw new IOException("Navigation is not centered / equal width.");
                foreach (var button in navigation)
                {
                    var origin = button.TranslatePoint(new Point(), NavigationCard);
                    if (button.ActualHeight < button.MinHeight - 0.1 || origin.Y < 11 ||
                        NavigationCard.ActualHeight - origin.Y - button.ActualHeight < 11)
                        throw new IOException("Navigation hover clearance is insufficient: " + button.Name);
                    var clip = VisualTreeHelper.GetClip(button);
                    if (clip != null && clip.Bounds.Height < button.ActualHeight - 0.5)
                        throw new IOException("Navigation has a vertical layout clip: " + button.Name);
                }
                foreach (FrameworkElement control in new FrameworkElement[] { DeckBox, FormatCombo, DeckRefreshButton, SaveSettingsButton, GamePathBox, BoxPathBox })
                {
                    var origin = control.TranslatePoint(new Point(), visual);
                    var bounds = new Rect(origin, control.RenderSize);
                    if (bounds.Bottom > size.Height || bounds.Right > size.Width || bounds.Top < 0 || control.Visibility != Visibility.Visible) throw new IOException($"Common control out of window: {control.Name}, bounds={bounds}, size={size}");
                    DependencyObject parent = control;
                    while ((parent = VisualTreeHelper.GetParent(parent)) != null)
                    {
                        if (parent is ScrollViewer scroller && scroller.Visibility == Visibility.Visible && control != scroller)
                        {
                            var relative = control.TranslatePoint(new Point(), scroller);
                            if (relative.Y + control.ActualHeight > scroller.ActualHeight + 1) throw new IOException($"Common control needs scrolling: {control.Name}, y={relative.Y}, h={control.ActualHeight}, viewport={scroller.ActualHeight}, size={size}");
                        }
                    }
                }
                Snapshot("Settings-" + size.Width + "x" + size.Height, (int)size.Width, (int)size.Height);
                foreach (var tab in new[] { "Traditional", "Rank", "Time" })
                {
                    SettingsTab(tab); visual.UpdateLayout();
                    var targets = tab switch
                    {
                        "Rank" => new FrameworkElement[] { LegendThresholdBox, RestartCooldownBox },
                        "Time" => new FrameworkElement[] { TimedStopBox, StopAfterHoursBox, StopAfterMinutesBox, TimedStopActionCombo, CancelTimedStopButton, ImmediateStopButton },
                        _ => new FrameworkElement[] { AutoConcedeBox, RotationNamesBox, WinRateThresholdBox }
                    };
                    foreach (var target in targets)
                    {
                        var bounds = new Rect(target.TranslatePoint(new Point(), PageHost), target.RenderSize);
                        if (bounds.Bottom > PageHost.ActualHeight || bounds.Right > PageHost.ActualWidth) throw new IOException("Advanced control not visible: " + target.Name);
                    }
                    Snapshot(tab + "-" + size.Width + "x" + size.Height, (int)size.Width, (int)size.Height);
                }
                SettingsTab("Basic"); visual.UpdateLayout();
            }
            Width = 1160; Height = 810; CompactLayout(810);
            visual.Measure(new Size(1160, 810)); visual.Arrange(new Rect(0, 0, 1160, 810)); visual.UpdateLayout();
            foreach (var page in new[] { "Account", "Stats", "Settings" })
            {
                Nav(page); visual.UpdateLayout();
                var wanted = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
                if (EngineActionPanel.Visibility != wanted || EngineStatusPanel.Visibility != wanted ||
                    EngineLogPanel.Visibility != wanted || (page != "Settings" && LogRow.Height.Value != 0))
                    throw new IOException("Engine controls/status/logs leaked into another page.");
            }
            Nav("Settings"); visual.UpdateLayout();
            var pathFixture = Path.Combine(data.DirectoryPath, "应用 中文 路径"); Directory.CreateDirectory(pathFixture);
            var fixtureBox = Path.Combine(pathFixture, "HSAng.exe"); var fixtureGame = Path.Combine(pathFixture, "Hearthstone.exe");
            File.WriteAllText(fixtureBox, "path fixture; never executed"); File.WriteAllText(fixtureGame, "path fixture; never executed");
            await InspectAsync(false, (game, _) => new(game ? fixtureGame : fixtureBox, "isolated path fixture"), inspectBridge: false);
            int dispatcherThread = Environment.CurrentManagedThreadId;
            var deckDiscovery = await FindGameForDeckRefreshAsync((_, path) =>
            {
                if (Environment.CurrentManagedThreadId == dispatcherThread || path != fixtureGame)
                    throw new IOException("Deck refresh did not snapshot the game path before worker discovery.");
                return new(path, "offline deck worker fixture");
            });
            Dispatcher.VerifyAccess();
            if (deckDiscovery.Path != fixtureGame) throw new IOException("Deck refresh path snapshot changed.");
            File.WriteAllText(Path.Combine(folder, "deck-refresh-thread-regression.txt"), "PASS: real deck refresh discovery snapshots WPF textbox on Dispatcher and executes discovery with a plain string on worker; returns to Dispatcher. No game launched.");
            if (BoxPathBox.Text != fixtureBox || GamePathBox.Text != fixtureGame)
                throw new IOException("Successful discovery did not fill installation text fields.");
            var pathsReloaded = new LocalData(data.DirectoryPath); await pathsReloaded.LoadAsync();
            if (pathsReloaded.Settings.BoxPath != fixtureBox || pathsReloaded.Settings.GamePath != fixtureGame)
                throw new IOException("Discovered paths were not persisted.");
            Snapshot("DetectedPaths", 1160, 810);
            BoxPathBox.Text = ""; GamePathBox.Text = ""; data.Settings.BoxPath = ""; data.Settings.GamePath = "";
            deckCatalog = [new("自检完整卡组", true, false, true), new("自检缺牌卡组", true, false, false)];
            DeckBox.Text = "手动输入不丢失"; ApplyDeckCatalog();
            if (!DeckBox.IsEditable || DeckBox.Text != "手动输入不丢失" || DeckBox.Items.Count != 1) throw new IOException("Deck dropdown lost input or included an incomplete deck.");
            DeckBox.SelectedIndex = 0;
            if (DeckBox.Text != "自检完整卡组") throw new IOException("Deck dropdown selection did not update editable text.");
            var editor = DeckBox.Template.FindName("PART_EditableTextBox", DeckBox) as TextBox;
            if (editor == null) throw new IOException("Editable ComboBox template has no editor.");
            editor.Text = "自定义套牌";
            if (DeckBox.Text != "自定义套牌") throw new IOException("Typing in deck editor did not update ComboBox.Text.");
            deckCatalog = []; ApplyDeckCatalog();
            WinsBox.Text = "2"; ReadSettings(); await data.SaveAsync();
            var controlsReloaded = new LocalData(data.DirectoryPath); await controlsReloaded.LoadAsync();
            if (controlsReloaded.Settings.Traditional.WinsBeforeConcede != 2 || controlsReloaded.Settings.DeckName != "自定义套牌") throw new IOException("Traditional settings or editable deck did not save.");
            WinsBox.Text = "invalid";
            bool invalid = false; try { ReadSettings(); } catch (IOException) { invalid = true; }
            if (!invalid || data.Settings.Traditional.WinsBeforeConcede != 2) throw new IOException("Invalid control values were not rejected atomically.");
            WinsBox.Text = "2";
            TimedStopBox.IsChecked = true; StopAfterHoursBox.Text = "168"; StopAfterMinutesBox.Text = "59";
            TimedStopActionCombo.SelectedIndex = (int)RunStopAction.ExitGameAndClients;
            ReadSettings(); await data.SaveAsync();
            var timeReloaded = new LocalData(data.DirectoryPath); await timeReloaded.LoadAsync();
            if (!timeReloaded.Settings.Time.Enabled || timeReloaded.Settings.Time.Hours != 168 || timeReloaded.Settings.Time.Minutes != 59 ||
                timeReloaded.Settings.Time.Action != RunStopAction.ExitGameAndClients || timedStop != null)
                throw new IOException("Time settings did not persist, or saving incorrectly armed the countdown.");
            foreach (var value in new[] { (Hours: "0", Minutes: "0"), (Hours: "169", Minutes: "0"), (Hours: "0", Minutes: "60"), (Hours: "invalid", Minutes: "1") })
            {
                StopAfterHoursBox.Text = value.Hours; StopAfterMinutesBox.Text = value.Minutes;
                bool rejected = false; try { ReadSettings(); } catch (IOException) { rejected = true; }
                if (!rejected || data.Settings.Time.Hours != 168 || data.Settings.Time.Minutes != 59)
                    throw new IOException("Invalid time input was not rejected atomically.");
            }
            StopAfterHoursBox.Text = "0"; StopAfterMinutesBox.Text = "1"; TimedStopActionCombo.SelectedIndex = -1;
            bool badActionRejected = false; try { ReadSettings(); } catch (IOException) { badActionRejected = true; }
            if (!badActionRejected || data.Settings.Time.Action != RunStopAction.ExitGameAndClients) throw new IOException("Invalid stop action was accepted.");
            TimedStopBox.IsChecked = false; StopAfterHoursBox.Text = "0"; StopAfterMinutesBox.Text = "0"; TimedStopActionCombo.SelectedIndex = 0;
            ReadSettings(); await data.SaveAsync();
            await SmokeTimedStopAsync(folder);
            if (Title != "HearthLeap" || GameInstallLabel.Text != "炉石传说" || BrandTitle.Text != "HearthLeap" || System.Reflection.Assembly.GetExecutingAssembly().GetName().Name != "HearthLeap" || PageTitle.Text != "引擎设置" || SettingsNav.Content.ToString() != "⚙  引擎设置" || AccountNav.Content.ToString() != "◈  玩家档案" || StatsNav.Content.ToString() != "▥  对战分析" || Icon == null || Icon.Width < 16)
                throw new IOException("Brand names or window icon are missing.");
            void CheckBrandLabels(DependencyObject parent)
            {
                if (parent is TextBlock label && label.Text.Contains("OPEN SOURCE", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Retired branding badge is still visible.");
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) CheckBrandLabels(VisualTreeHelper.GetChild(parent, i));
            }
            CheckBrandLabels(visual);
            // Offline template-state test, no desktop mouse movement or real game actions.
            var key = typeof(UIElement).GetField("IsMouseOverPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey
                ?? throw new IOException("WPF hover-state key is unavailable for the isolated template test.");
            MotionSettings.SetEnabled(this, true); StartButton.ApplyTemplate(); StartButton.SetValue(key, true);
            AccountNav.ApplyTemplate(); AccountNav.SetValue(key, true);
            await Task.Delay(300);
            var glow = (Border)StartButton.Template.FindName("hoverGlow", StartButton);
            var sweep = (Border)StartButton.Template.FindName("sweep", StartButton);
            var surface = (Grid)StartButton.Template.FindName("surface", StartButton);
            if (glow.Opacity <= 0.1 || sweep.Opacity <= 0.05 || Math.Abs(((ScaleTransform)surface.RenderTransform).ScaleX - 1) > 0.001) throw new IOException("Hover glow/sweep did not activate or changed button bounds.");
            var firstGlow = glow.Opacity; await Task.Delay(900);
            if (Math.Abs(glow.Opacity - firstGlow) < 0.01) throw new IOException("Held hover pulse did not progress.");
            Snapshot("Hover", 1160, 810);
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                Snapshot("NavigationHover-" + (int)(scale * 100) + "percent", 1160, 810, scale);
            // Check every shared switch, not just navigation. Reproduce hover after checked state,
            // focus+hover, rapid on/off, accessibility mode changes and every intermediate frame.
            var focusKey = typeof(UIElement).GetField("IsKeyboardFocusedPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as DependencyPropertyKey
                ?? throw new IOException("WPF focus-state key unavailable.");
            var switchChecks = new List<string>();
            foreach (var tab in new[] { "Basic", "Traditional", "Rank", "Time" })
            {
                SettingsTab(tab); visual.UpdateLayout();
                var switches = tab switch
                {
                    "Basic" => new[] { AutoBox, AutoQueue, ReadOnly },
                    "Traditional" => new[] { AutoConcedeBox, RopeProtectionBox, WinPatternBox, LowWinRateBox, RotateDecksBox },
                    "Rank" => new[] { StopAtRankBox, ControlRankBox, LegendStopBox, RestartBoxToggle },
                    _ => new[] { TimedStopBox }
                };
                foreach (var control in switches)
                {
                    control.ApplyTemplate(); var checkedBefore = control.IsChecked;
                    var track = (Border)control.Template.FindName("track", control);
                    var knob = (System.Windows.Shapes.Ellipse)control.Template.FindName("knob", control);
                    var decoration = (Border)control.Template.FindName("switchHover", control);
                    var focus = (Border)control.Template.FindName("switchFocus", control);
                    void CheckKnob()
                    {
                        visual.UpdateLayout();
                        var bounds = new Rect(knob.TranslatePoint(new Point(), track), knob.RenderSize);
                        if (track.ActualWidth != 40 || track.ActualHeight != 24 || track.BorderThickness != new Thickness(0) ||
                            bounds.Left < 2.9 || bounds.Right > 37.1 || Math.Abs(bounds.Top - 3) > 0.1 || Math.Abs(bounds.Bottom - 21) > 0.1)
                            throw new IOException($"Switch thumb escapes fixed track: {control.Name}, {bounds}.");
                    }
                    control.IsChecked = true; await Task.Delay(190); CheckKnob();
                    var resting = new Rect(knob.TranslatePoint(new Point(), track), knob.RenderSize);
                    control.SetValue(key, true); await Task.Delay(190); CheckKnob();
                    if (decoration.Opacity < 0.99 || new Rect(knob.TranslatePoint(new Point(), track), knob.RenderSize) != resting)
                        throw new IOException("Hover shifted switch thumb: " + control.Name);
                    control.SetValue(focusKey, true); CheckKnob();
                    if (focus.Opacity < 0.99) throw new IOException("Switch keyboard focus absent: " + control.Name);
                    control.SetValue(focusKey, false);
                    for (var cycle = 0; cycle < 4; cycle++)
                    {
                        control.IsChecked = cycle % 2 == 0; control.SetValue(key, cycle % 2 != 0);
                        await Task.Delay(35); CheckKnob();
                    }
                    control.IsChecked = false; control.SetValue(key, true); await Task.Delay(200); CheckKnob();
                    if (Math.Abs(((TranslateTransform)knob.RenderTransform).X) > 0.01) throw new IOException("Switch off position stuck: " + control.Name);
                    MotionSettings.SetEnabled(this, false); control.IsChecked = true; CheckKnob();
                    if (Math.Abs(((TranslateTransform)knob.RenderTransform).X - 16) > 0.01) throw new IOException("Switch accessibility checked position wrong: " + control.Name);
                    control.SetValue(key, false); control.IsChecked = checkedBefore; MotionSettings.SetEnabled(this, true);
                    switchChecks.Add(control.Name);
                }
                // Hover all normal buttons on this visible settings page; no bounds change allowed.
                static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
                {
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    {
                        var child = VisualTreeHelper.GetChild(parent, i); yield return child;
                        foreach (var nested in Descendants(child)) yield return nested;
                    }
                }
                foreach (var button in Descendants(visual).OfType<Button>().Where(b => b.Visibility == Visibility.Visible && b.IsEnabled))
                {
                    button.ApplyTemplate(); var face = button.Template.FindName("surface", button) as Grid;
                    if (face == null) continue;
                    var sizeBefore = button.RenderSize;
                    button.SetValue(key, true); await Task.Delay(20); visual.UpdateLayout();
                    if (button.RenderSize != sizeBefore || Math.Abs(((ScaleTransform)face.RenderTransform).ScaleX - 1) > 0.001)
                        throw new IOException("Button hover changed geometry: " + button.Content);
                    button.SetValue(key, false); button.SetValue(key, true); button.SetValue(key, false);
                }
                foreach (var control in switches) { control.IsChecked = true; control.SetValue(key, true); }
                await Task.Delay(220); visual.UpdateLayout();
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 }) Snapshot("SwitchHover-" + tab + "-" + (int)(scale * 100) + "percent", 1160, 810, scale);
                foreach (var control in switches) { control.SetValue(key, false); }
            }
            UpdateSettings(); SettingsTab("Basic");
            File.WriteAllText(Path.Combine(folder, "switch-regression.txt"), "PASS: " + string.Join(", ", switchChecks) + "; fixed 40x24 track, 18x18 thumb stays within 3-DIP gutter; checked/off/hover/focus/rapid changes/motion disabled; all visible Button hover bounds unchanged. Offscreen render scales only, not physical DPI monitors.");
            MotionSettings.SetEnabled(this, false); await Task.Delay(260);
            if (glow.Opacity > 0.01 || sweep.Opacity > 0.01 || Math.Abs(((ScaleTransform)surface.RenderTransform).ScaleX - 1) > 0.01) throw new IOException("Reduced-motion mode did not stop hover animations.");
            StartButton.SetValue(key, false); AccountNav.SetValue(key, false); Snapshot("ReducedMotion", 1160, 810);
            LogBox.Clear();
            var diagnosticPath = Path.Combine(data.DirectoryPath, "controller-0.1.8.log");
            Log("[23:14:22] [23:14:22] UnityBridge 协议 1 已连接，PID=35776");
            await Task.Run(() => Log("盒子AI CEF 扫描[完整扫描]：OpenProcess=成功，payload 数=0。"));
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            Log("[推荐] 打出手牌 13：客户端内部 OptionsPacket 确认该动作合法");
            Log("[传统执行] 打出手牌 13 已通过内部 OptionsPacket 提交");
            Log("[推荐] 等待：等待客户端生成本手 OptionsPacket，暂不读取或执行旧推荐");
            Log("等待客户端生成本手 OptionsPacket，暂不读取或执行旧推荐");
            if (LogBox.Text.Contains("PID=") || LogBox.Text.Contains("OpenProcess") || !LogBox.Text.Contains("盒子建议出牌") || !LogBox.Text.Contains("已出牌") ||
                LogBox.Text.Split("等待游戏状态更新。").Length != 2 || Recommendation.Text != "等待游戏状态更新。")
                throw new IOException("Player feed or recommendation field retained technical details or duplicate waits.");
            if (!File.ReadAllText(diagnosticPath).Contains("OpenProcess=成功") || !File.ReadAllText(diagnosticPath).Contains("PID=35776"))
                throw new IOException("Full technical diagnostic details were lost.");
            File.WriteAllText(Path.Combine(folder, "player-log.txt"), LogBox.Text);
            Snapshot("PlayerLogs", 1160, 810);
            ShowAccount(JsonSerializer.SerializeToElement(new { loggedIn=true, playerName="离线昵称测试", gold=123, ready=true }));
            if (AccountName.Text != "离线昵称测试" || IdentityHint.Text.Length == 0 || FindName("AliasBox") != null) throw new IOException("Automatic player-name presentation is missing or still asks for manual input.");
            Nav("Account"); visual.UpdateLayout(); Snapshot("AutomaticPlayerName", 1160, 810);
            ShowAccount(JsonSerializer.SerializeToElement(new { loggedIn=false, playerName="过期玩家名" }));
            if (AccountName.Text != "等待账号信息") throw new IOException("Logged-out client retained a stale player name.");
            Nav("Settings"); visual.UpdateLayout();
            BoxPathBox.Text = Path.Combine(data.DirectoryPath, "invalid.exe");
            SaveSettingsClick(this, new RoutedEventArgs());
            await Task.WhenAll(foregroundOperations.ToArray());
            if (!LogBox.Text.Contains("炉石盒子路径无效")) throw new IOException("GUI invalid path was not rejected.");
            // Create an invisible native window to exercise the actual WPF Closing event.
            new WindowInteropHelper(this).EnsureHandle();
            bool installationFinished = false;
            installing = Task.Run(async () => { await Task.Delay(180); installationFinished = true; });
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Closed += (_, _) => ended.TrySetResult();
            var closePlatform = runStopPlatform as SmokeRunStopPlatform ?? throw new IOException("Smoke test did not use an isolated stop platform.");
            closePlatform.Events.Clear();
            using var closeSource = new CancellationTokenSource();
            run = closeSource;
            BeginTimedStop(new() { Enabled = true, Minutes = 1, Action = RunStopAction.Shutdown }, closeSource, () => Task.CompletedTask, [], []);
            Close(); await ended.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (!installationFinished || !File.Exists(Path.Combine(data.DirectoryPath, "settings.json")) || timedStop != null || !closeSource.IsCancellationRequested || closePlatform.Events.Count != 0)
                throw new IOException("GUI close did not drain installation/save and cancel the timer without external effects.");
            File.WriteAllText(Path.Combine(folder, "smoke.txt"), "PASS: three pages + traditional panel rendered; engine operations/status/log confined to Engine Analysis; equal centered top navigation with 44-DIP minimum and vertical hover clearance/no layout clip; 100/125/150/200 percent offscreen hover renders (not physical monitor DPI tests); common controls visible without scroll at 1160x810 and 960x700; isolated discovery fills and persists both install paths; editable dropdown selection/typing/input preservation; traditional settings saved/reloaded; invalid numbers rejected atomically; hover glow/sweep with fixed bounds and held pulse; all 12 switch templates checked/off/hover/focus/rapid transitions/motion disabled; player-friendly Chinese feed vs full diagnostic file, timestamp normalization and duplicate-wait suppression, worker-thread logging; Windows accessibility motion support retained; automatic nickname presentation; invalid path rejected; actual hidden WPF window close waited for installation and save. No desktop mouse moved; no game/box started; isolated fixtures only.");
            File.AppendAllText(Path.Combine(folder, "smoke.txt"), Environment.NewLine + "PASS: time page exclusive/visible at both sizes; hours/minutes/action validated atomically and saved/reloaded without arming; all 13 switches including timer verified; fake monotonic timer expiry, user cancellation, manual stop, cancelled worker drain, failed save and hidden-window close. Fake platform only: no real process exit or shutdown command executed.");
            Application.Current.Shutdown(0);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(folder ?? AppContext.BaseDirectory, "smoke-error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }
}

