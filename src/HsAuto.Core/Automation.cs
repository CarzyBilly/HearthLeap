using System.Diagnostics;
using System.Text.Json;
using HsAuto.Core.Automation;
using HsAuto.Core.Bot;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;
using HsAuto.Core.Strategy;

namespace HsAuto.Open;

public sealed class Automation
{
    public event Action<string> Log;
    public event Action<ConstructedGameState> State;
    public event Action<JsonElement> Account;
    public event Action MatchesChanged;
    public async Task RunAsync(int pid, LocalData data, CancellationToken ct)
    {
        var options = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(data.Settings));
        options.Traditional ??= new TraditionalSettings(); options.Traditional.Validate(options.Format);
        var controls = options.Traditional;
        long targetStarted;
        using (var target = Process.GetProcessById(pid)) targetStarted = target.StartTime.ToUniversalTime().Ticks;
        var client = new UnityBridgeClient("HsAuto.OpenBridge." + pid);
        var format = ConstructedSettings.ParseFormat(options.Format);
        var reader = new UnityBridgeConstructedGameStateReader(client, format); reader.Log += m => Log?.Invoke(m);
        var source = new NeteaseBoxRecommendationSource(enableDiagnostics: true);
        var strategy = new NeteaseBoxConstructedStrategy(source, enableDiagnostics: true, enableMissingOptionsRecovery: false); strategy.Log += m => Log?.Invoke(m);
        var executor = new ConstructedActionExecutor(client, reader); executor.Log += m => Log?.Invoke(m);
        Log?.Invoke("操作节奏：每次独立卡牌操作前随机等待 1.0～1.8 秒。");
        var navigator = new HearthstoneConstructedNavigator(client, pid); navigator.Log += m => Log?.Invoke(m);
        var session = new TraditionalSession(controls);
        var health = new RecommendationHealth(waitingLimit: TimeSpan.FromSeconds(3), executionWaitingLimit: TimeSpan.FromSeconds(3));
        var pending = new PendingGameAction();
        var supervisor = new ScriptRecoverySupervisor(restartCooldown: TimeSpan.FromSeconds(Math.Max(30, controls.RestartCooldownSeconds)));
        Log?.Invoke("当前推荐源：盒子本机推荐。没有启用原版盒子AI服务直连；Bridge 连接不等于已有 AI 建议。");
        var rope = new NeteaseConstructedStallConcedeTracker(); rope.SetEnabled(controls.RopeProtection && !options.ReadOnly);
        string selectedDeck = options.DeckName; bool rotationPending = false;
        navigator.BeforeMatchStartCheck = async (_, token) =>
        {
            if (controls.StopAtRank || controls.StopAtLegend || controls.ControlRank)
            {
                var response = await client.SendAsync("accountDetails", timeoutMs: 10000, cancellationToken: token);
                var rank = response.Ok ? RankReader.Parse(response.Data, options.Format) : HearthstoneConstructedRankSnapshot.Unavailable(options.Format);
                if (!rank.Available || rank.IsNewPlayer) throw new IOException("无法确认当前排位段位，已停止匹配；请等游戏账号数据就绪后重试。");
                Account?.Invoke(response.Data.Clone()); session.SetRank(rank);
                var reason = RankReader.StopReason(controls, rank);
                if (reason.Length > 0) throw new EnginePolicyCompletedException(reason);
            }
            return MatchStartPreflightResult.AllowStart;
        };
        string currentMatch = ""; var started = DateTimeOffset.Now; var lastReason = ""; bool matchActive = false;
        string lastExecutionWait = "";
        async Task Generation(CancellationToken generationToken)
        {
            health.Reset();
        var ping = await client.SendAsync("ping", cancellationToken: generationToken);
        if (!BridgeHandshake.Matches(ping, pid)) throw new IOException("未连接开源插件。请在引擎设置安装/修复插件，然后重启炉石。");
        // Advanced commands were added in 0.1.1. Do not send them to a stale installed plugin.
        if ((controls.AutoConcede || controls.WinThenConcede || controls.ControlRank || controls.LowWinRateConcede || controls.RopeProtection) &&
            (!ping.Data.TryGetProperty("version", out var ver) || !Version.TryParse(ver.GetString(), out var version) || version < new Version(0, 1, 1)))
            throw new IOException("传统投降控制需要 0.1.1 插件。请退出炉石并安装/修复插件后重启游戏。");
        Log?.Invoke("UnityBridge 协议 1 已连接，PID=" + pid);
        var details = await client.SendAsync("accountDetails", timeoutMs: 10000, cancellationToken: generationToken);
        if (details.Ok) Account?.Invoke(details.Data.Clone());
            if (details.Ok) session.SetRank(RankReader.Parse(details.Data, options.Format));
            await strategy.InitializeAsync(new ConstructedStrategyRuntimeContext(pid, client.PipeName), generationToken);
        while (!generationToken.IsCancellationRequested)
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != targetStarted)
                throw new IOException("目标炉石已退出或身份变化，保持等待；不会改绑其他账号。");
            var observed = await reader.ReadAsync(generationToken); var state = observed.State;
            StateSafety.RequireReadable(state); State?.Invoke(state); rope.ObserveState(state);
            bool hadPending = pending.HasPending;
            if (!pending.Resolve(state))
                throw new IOException("上一操作尚未确认完成，正在自动重连读取状态；不会重复提交同一操作。");
            if (hadPending) supervisor.MarkHealthy();
            executor.ObservePhase(state.Phase);
            strategy.ObserveState(state);
            if (!matchActive && state.Phase is ConstructedPhase.Mulligan or ConstructedPhase.LocalTurn or ConstructedPhase.OpponentTurn)
            { currentMatch = pid + ":" + Guid.NewGuid().ToString("N"); started = DateTimeOffset.Now; matchActive = true; session.BeginMatch(); }
            var settings = controls.Runtime(options, selectedDeck) with { EnableNeteaseBoxAutoRecovery = false };
            if (state.Phase == ConstructedPhase.GameOver)
            {
                if (matchActive && state.Result == ConstructedGameResult.Unknown)
                { if (lastReason != "result") Log?.Invoke("等待游戏确认胜负结果，暂不重新匹配。"); lastReason = "result"; await Task.Delay(options.PollMs, generationToken); continue; }
                if (matchActive && session.RecordResult(currentMatch, state.Result))
                {
                    await data.AddMatchAsync(new(currentMatch, started, DateTimeOffset.Now, options.Format, state.Result.ToString()));
                    matchActive = false; rope.ObserveGameCompleted(); MatchesChanged?.Invoke();
                    rotationPending = controls.RotateDecks && session.Completed % controls.RotateEvery == 0;
                    if (session.ConsecutiveRopeConcedes >= 3) throw new IOException("连续 3 局烧绳保护投降，保持等待；请检查盒子是否实际提供推荐。");
                }
                if (!settings.DryRun && settings.AutoQueue) await navigator.TryDismissPostGameLayerOnlyAsync(settings, generationToken);
                else { if (lastReason != "gameover") Log?.Invoke("本局结束，战绩已记录。等待下一局；脚本保持运行。"); lastReason = "gameover"; }
            }
            else if (state.Phase is ConstructedPhase.Queue or ConstructedPhase.Unknown)
            {
                if (!settings.DryRun && settings.AutoQueue)
                {
                    if (rotationPending)
                    {
                        var decks = await client.SendAsync(DeckCatalog.Command, timeoutMs: 8000, cancellationToken: generationToken);
                        if (!decks.Ok) throw new IOException("轮换卡组列表尚未就绪，已停止以免选错套牌：" + decks.Error);
                        selectedDeck = session.Rotate(selectedDeck, DeckCatalog.Names(DeckCatalog.Parse(decks.Data), options.Format));
                        rotationPending = false; settings = controls.Runtime(options, selectedDeck) with { EnableNeteaseBoxAutoRecovery = false };
                        Log?.Invoke("轮换卡组：" + selectedDeck);
                    }
                    await navigator.TryPrepareConstructedAsync(settings, generationToken);
                }
                else if (lastReason != "queue") { Log?.Invoke("等待进入对局（自动匹配未启用）。"); lastReason = "queue"; }
            }
            else
            {
                // An explicit policy concede does not wait for the AI service to produce a recommendation.
                IReadOnlyList<ConstructedAction> actions;
                var concede = session.Concede(state, null, DateTimeOffset.UtcNow);
                if (concede != null) actions = new[] { concede };
                else
                {
                    actions = await strategy.DecideAsync(state, settings, generationToken);
                    strategy.TryGetLatestWinRate(state.MatchId, out var rate);
                    concede = session.Concede(state, rate, DateTimeOffset.UtcNow);
                    if (concede != null) actions = new[] { concede };
                }
                var missing = health.Observe(state, actions, settings.DryRun);
                if (missing != null)
                {
                    Log?.Invoke("[推荐未就绪] " + missing);
                    if (!settings.DryRun) throw new RecommendationUnavailableException(missing);
                }
                if (settings.DryRun && actions.Any(a => a.Type is not (ConstructedActionType.NoOp or ConstructedActionType.Wait))) supervisor.MarkHealthy();
                var decision = new ConstructedDecisionEnvelope { MatchId = state.MatchId, ObservedStateSequence = observed.Sequence, StrategyName = strategy.Name, Actions = actions, DryRun = settings.DryRun };
                decision = rope.TryCreateConcedeDecision(state, decision, true) ?? decision;
                actions = decision.Actions;
                var action = actions.FirstOrDefault(a => a.Type is not (ConstructedActionType.NoOp or ConstructedActionType.Wait or ConstructedActionType.MulliganKeep));
                var message = string.Join(" / ", actions.Select(a => a.ToString()));
                if (message != lastReason) { Log?.Invoke((settings.DryRun ? "[只读] " : "[推荐] ") + message); lastReason = message; }
                if (action != null && !settings.DryRun)
                {
                    pending.Begin(state, action);
                    var execution = await executor.ExecuteAsync(decision, generationToken);
                    if (!execution.Attempted) pending.Clear();
                    var executionStall = health.ObserveExecution(state, execution);
                    if (executionStall != null) throw new IOException(executionStall);
                    if (execution.Attempted && execution.Succeeded)
                    {
                        lastExecutionWait = "";
                        bool confirmed = action.Type == ConstructedActionType.MulliganReplace;
                        var until = DateTimeOffset.UtcNow.AddSeconds(8);
                        while (!confirmed && DateTimeOffset.UtcNow < until)
                        {
                            await Task.Delay(300, generationToken); var after = await reader.ReadAsync(generationToken);
                            StateSafety.RequireReadable(after.State); State?.Invoke(after.State);
                            confirmed = ActionConfirmation.Changed(execution.Action ?? action, state, after.State);
                        }
                        if (!confirmed) throw new IOException("动作已提交但未观察到完成，正在自动重连，避免重复出牌。动作：" + action);
                        pending.Clear(); supervisor.MarkHealthy();
                        strategy.OnActionExecutionSucceeded(state, execution.Action ?? action); rope.RecordExecution(state, execution);
                        if (action.Type == ConstructedActionType.Concede) session.RecordConcede(execution.Action ?? action);
                    }
                    else if (execution.Attempted) throw new IOException("动作失败，等待重新确认局面后自动恢复：" + execution.Reason);
                    else if (execution.Reason != lastExecutionWait)
                    {
                        // A recommendation can be ready while the opening animation
                        // or game state is not. Report the actual executor wait.
                        Log?.Invoke("[传统执行] " + execution.Reason);
                        lastExecutionWait = execution.Reason;
                    }
                }
            }
            await Task.Delay(options.PollMs, generationToken);
        }
        }
        await supervisor.RunAsync(Generation,
            token => NeteaseBoxRecommendationSource.RestartBoxForTargetAsync(pid, options.BoxPath, Log, token, targetStarted),
            allowBoxRestart: !options.ReadOnly, Log, ct);
    }
}

