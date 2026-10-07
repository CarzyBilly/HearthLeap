using System.Reflection;
using HsAuto.Core.Automation;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;
using HsAuto.Core.Strategy;
using HsAuto.Open;

static class StartupRecommendationCases
{
    static void Need(bool condition, string message = "开局推荐断言失败")
    { if (!condition) throw new Exception(message); }
    static bool Confirm(IReadOnlyList<ConstructedAction> actions) => actions.Any(a => a.Type == ConstructedActionType.ConfirmMulligan);
    static bool Waits(IReadOnlyList<ConstructedAction> actions) => actions.All(a => a.Type is ConstructedActionType.Wait or ConstructedActionType.NoOp);
    static ConstructedGameState Opening(string match = "opening-fixture", string entity = "17") => new()
    {
        MatchId = match, Phase = ConstructedPhase.Mulligan, Turn = 0,
        Hand = [new() { EntityId = entity, CardId = "FIXTURE_CARD", Zone = ConstructedZone.Hand }]
    };
    static ConstructedGameState FirstTurn(int packet = 1, int turn = 1) => new()
    {
        MatchId = "opening-fixture", Phase = ConstructedPhase.LocalTurn, Turn = turn, ManaTotal = 1,
        Tags = new Dictionary<string, string> { ["unity.optionsPacketId"] = packet.ToString() }
    };
    static NeteaseBoxRecommendationObservation Event(Clock clock, string name = "replace", int option = -1, int pid = 101) =>
        new(new() { OptionId = option, Data = [new() { ActionName = name }] }, clock.GetUtcNow(), pid, 0x1000);

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var settings = new ConstructedSettings();
        async Task<(Clock clock, Source source, NeteaseBoxConstructedStrategy strategy)> Setup(int initSeconds = 0)
        {
            var clock = new Clock(); var source = new Source(clock) { InitSeconds = initSeconds };
            var strategy = new NeteaseBoxConstructedStrategy(source, timeProvider: clock, enableMissingOptionsRecovery: false);
            await strategy.InitializeAsync(new(123, "offline-fixture"), default);
            return (clock, source, strategy);
        }
        await check("开局：初始化耗时10秒不丢弃期间捕获的起手建议", async () =>
        {
            var (clock, source, strategy) = await Setup(10);
            Need(source.CapturedDuringInit < clock.GetUtcNow().AddSeconds(-3));
            Need(Confirm(await strategy.DecideAsync(Opening(), settings, default)));
        });
        await check("开局：初始化期间首回合建议保留但仍校验当前选项编号", async () =>
        {
            var (clock, source, strategy) = await Setup(10);
            source.Events = [Event(clock, "end_turn", 1) with { CapturedAt = source.CapturedDuringInit }];
            Need((await strategy.DecideAsync(FirstTurn(), settings, default)).Any(a => a.Type == ConstructedActionType.EndTurn));
            Need(Waits(await strategy.DecideAsync(FirstTurn(2), settings, default)));
        });
        await check("开局：先观察大厅再入新局仍拒绝旧建议", async () =>
        {
            var (clock, source, strategy) = await Setup();
            await strategy.DecideAsync(Opening() with { Phase = ConstructedPhase.Queue }, settings, default);
            clock.Advance(12);
            Need(Waits(await strategy.DecideAsync(Opening(), settings, default)));
        });
        await check("开局：已匹配当前手牌的建议等待25秒后仍能换牌", async () =>
        {
            var (clock, source, strategy) = await Setup();
            Need(Confirm(await strategy.DecideAsync(Opening(), settings, default)));
            source.Events = []; clock.Advance(25);
            Need(Confirm(await strategy.DecideAsync(Opening(), settings, default)));
            Need(source.Relocations == 0 && source.Restarts == 0);
        });
        foreach (string change in new[] { "hand", "match", "phase" })
            await check("开局：暂存起手建议在" + change + "改变后失效", async () =>
            {
                var (clock, source, strategy) = await Setup();
                await strategy.DecideAsync(Opening(), settings, default); source.Events = []; clock.Advance(25);
                var changed = change == "hand" ? Opening(entity: "18") : change == "match" ? Opening("new-match") : FirstTurn();
                Need(Waits(await strategy.DecideAsync(changed, settings, default)));
            });
        await check("开局：起手暂存有60秒上限且轮询不会续期", async () =>
        {
            var (clock, source, strategy) = await Setup();
            await strategy.DecideAsync(Opening(), settings, default); source.Events = []; clock.Advance(35);
            Need(Confirm(await strategy.DecideAsync(Opening(), settings, default))); clock.Advance(26);
            Need(Waits(await strategy.DecideAsync(Opening(), settings, default)));
        });
        await check("开局：其他CEF候选的无关指令不覆盖有效起手建议", async () =>
        {
            var (clock, source, strategy) = await Setup();
            await strategy.DecideAsync(Opening(), settings, default); clock.Advance(25);
            source.Events = [Event(clock, "end_turn", 1, 202)];
            Need(Confirm(await strategy.DecideAsync(Opening(), settings, default)));
        });
        await check("开局：来源窗口前的旧建议不可暂存或执行", async () =>
        {
            var (clock, source, strategy) = await Setup();
            source.Events = [Event(clock) with { CapturedAt = clock.GetUtcNow().AddMinutes(-1) }];
            Need(Waits(await strategy.DecideAsync(Opening(), settings, default))); source.Events = []; clock.Advance(25);
            Need(Waits(await strategy.DecideAsync(Opening(), settings, default)));
        });
        foreach (bool unrelated in new[] { false, true })
            await check("开局：" + (unrelated ? "只有无关建议" : "建议迟到") + "时5秒后自动重定位无需手动重启", async () =>
            {
                var (clock, source, strategy) = await Setup();
                source.Events = unrelated ? [Event(clock, "end_turn", 1)] : [];
                source.AfterRelocate = () => source.Events = [Event(clock)];
                Need(Waits(await strategy.DecideAsync(Opening(), settings, default))); clock.Advance(4);
                Need(Waits(await strategy.DecideAsync(Opening(), settings, default)) && source.Relocations == 0); clock.Advance(1);
                Need(Confirm(await strategy.DecideAsync(Opening(), settings, default)) && source.Relocations == 1 && source.Restarts == 0);
            });
        await check("开局：重定位不能复活旧时间戳建议", async () =>
        {
            var (clock, source, strategy) = await Setup(); source.Events = [];
            source.AfterRelocate = () => source.Events = [Event(clock) with { CapturedAt = clock.GetUtcNow().AddMinutes(-1) }];
            await strategy.DecideAsync(Opening(), settings, default); clock.Advance(5);
            Need(Waits(await strategy.DecideAsync(Opening(), settings, default)) && source.Relocations == 1);
        });
        await check("开局：首回合错编号不执行，重新定位后只用当前编号", async () =>
        {
            var (clock, source, strategy) = await Setup(); source.Events = [Event(clock, "end_turn", 99)];
            source.AfterRelocate = () => source.Events = [Event(clock, "end_turn", 1)];
            Need(Waits(await strategy.DecideAsync(FirstTurn(), settings, default))); clock.Advance(5);
            Need((await strategy.DecideAsync(FirstTurn(), settings, default)).Any(a => a.Type == ConstructedActionType.EndTurn));
            Need(source.Relocations == 1);
        });
        await check("开局：持续缺建议最多重定位2次，不重启盒子或无限重试", async () =>
        {
            var (clock, source, strategy) = await Setup(); source.Events = [];
            for (int i = 0; i < 12; i++) { await strategy.DecideAsync(Opening(), settings, default); clock.Advance(5); }
            Need(source.Relocations == 2 && source.Restarts == 0);
        });
        await check("开局：正常起手和后续回合不触发额外重定位", async () =>
        {
            var (clock, source, strategy) = await Setup();
            await strategy.DecideAsync(Opening(), settings, default); clock.Advance(8);
            await strategy.DecideAsync(Opening(), settings, default);
            source.Events = []; await strategy.DecideAsync(FirstTurn(turn: 2), settings, default); clock.Advance(8);
            await strategy.DecideAsync(FirstTurn(turn: 2), settings, default);
            Need(source.Relocations == 0 && source.Restarts == 0);
        });
        await check("开局：已成功操作首回合后不再重定位正常读取器", async () =>
        {
            var (clock, source, strategy) = await Setup();
            source.Events = [Event(clock, "end_turn", 1)];
            var actions = await strategy.DecideAsync(FirstTurn(), settings, default);
            strategy.OnActionExecutionSucceeded(FirstTurn(), actions.Single()); source.Events = [];
            await strategy.DecideAsync(FirstTurn(2), settings, default); clock.Advance(8);
            await strategy.DecideAsync(FirstTurn(2), settings, default); Need(source.Relocations == 0);
        });
        await check("开局：起手已确认后不再重复建议或重定位", async () =>
        {
            var (clock, source, strategy) = await Setup();
            var actions = await strategy.DecideAsync(Opening(), settings, default);
            strategy.OnActionExecutionSucceeded(Opening(), actions.Last()); clock.Advance(25);
            Need(Waits(await strategy.DecideAsync(Opening(), settings, default)) && source.Relocations == 0);
        });
        await check("开局：用户停止在重定位时传播取消，不转为继续运行", async () =>
        {
            var (clock, source, strategy) = await Setup(); source.Events = []; using var stop = new CancellationTokenSource();
            source.AfterRelocate = () => { stop.Cancel(); stop.Token.ThrowIfCancellationRequested(); };
            await strategy.DecideAsync(Opening(), settings, stop.Token); clock.Advance(5); bool cancelled = false;
            try { await strategy.DecideAsync(Opening(), settings, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled && source.Restarts == 0);
        });
        await check("开局：读取器重定位清除位置缓存但保留首次时间戳和推荐窗口", () =>
        {
            // Invoke only cache bookkeeping; never scan live browser/game processes.
            var source = new NeteaseBoxRecommendationSource(); var type = source.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var raw = new NeteaseBoxRecommendationObservation(new() { OptionId = 1, Data = [new() { ActionName = "end_turn" }] }, DateTimeOffset.MinValue, 101, 0x1000);
            var normalize = type.GetMethod("NormalizeMemoryObservation", flags)!;
            var first = (NeteaseBoxRecommendationObservation)normalize.Invoke(source, [raw])!;
            ((HashSet<int>)type.GetField("_rendererProcessIds", flags)!.GetValue(source)!).Add(101);
            var epoch = DateTimeOffset.UtcNow.AddSeconds(-3); source.SetRecommendationEpoch(epoch);
            type.GetMethod("CacheObservations", flags)!.Invoke(source, [new[] { raw }]);
            type.GetMethod("InvalidateRendererLocations", flags)!.Invoke(source, null);
            var second = (NeteaseBoxRecommendationObservation)normalize.Invoke(source, [raw])!;
            Need(first.CapturedAt == second.CapturedAt);
            Need(((HashSet<int>)type.GetField("_rendererProcessIds", flags)!.GetValue(source)!).Count == 0);
            Need((DateTimeOffset)type.GetField("_recommendationEpoch", flags)!.GetValue(source)! == epoch);
            Need(((Dictionary<int, List<NeteaseBoxRecommendationObservation>>)type.GetField("_latestByOptionId", flags)!.GetValue(source)!).Count == 1);
            return Task.CompletedTask;
        });
        await check("开局：玩家日志显示等待动画和自动读取而非英文诊断", () =>
        {
            Need(PlayerLog.ForDisplay("[传统执行] 起手换牌状态尚未稳定，等待下一帧再操作") == "起手建议已收到，等待开场动画和手牌就绪。");
            Need(PlayerLog.ForDisplay("开局建议尚未就绪，已重新定位盒子建议读取模块；无需停止再开始。")!.Contains("无需停止再开始"));
            return Task.CompletedTask;
        });
    }
    sealed class Clock : TimeProvider
    {
        DateTimeOffset now = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    sealed class Source(Clock clock) : INeteaseBoxRecommendationSource
    {
        public event Action<string>? Log;
        public int InitSeconds, Relocations, Restarts;
        public DateTimeOffset Epoch, CapturedDuringInit;
        public IReadOnlyList<NeteaseBoxRecommendationObservation> Events = [];
        public Action? AfterRelocate;
        public Task InitializeAsync(int pid, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Events = [Event(clock)]; CapturedDuringInit = clock.GetUtcNow();
            clock.Advance(InitSeconds); Log?.Invoke("offline fixture initialized"); return Task.CompletedTask;
        }
        public void SetRecommendationEpoch(DateTimeOffset epoch) => Epoch = epoch;
        public Task<IReadOnlyList<NeteaseBoxRecommendationObservation>> ReadAsync(int option, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(Events); }
        public Task<bool> RelocateAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); Relocations++; AfterRelocate?.Invoke(); return Task.FromResult(true); }
        public Task<bool> RestartAndReconnectAsync(int pid, string reason, CancellationToken token)
        { Restarts++; return Task.FromResult(true); }
    }
}
