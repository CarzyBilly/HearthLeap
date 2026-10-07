using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;
using HsAuto.Open;
using HsAuto.UnityBridge;

internal static class CardPacingCases
{
    static void Need(bool ok, string message = "Card pacing assertion failed.")
    { if (!ok) throw new Exception(message); }

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("卡牌节奏：包含1000和1800边界，每个动作独立采样", async () =>
        {
            int sample = 0; var waits = new List<int>();
            var pacing = new CardOperationPacing((min, max) =>
            {
                Need(min == 1000 && max == 1801);
                return sample++ == 0 ? 1000 : 1800;
            }, (ms, _) => { waits.Add(ms); return Task.CompletedTask; });
            await pacing.WaitAsync(ConstructedActionType.PlayCard, default);
            await pacing.WaitAsync(ConstructedActionType.PlayCardWithTarget, default);
            Need(sample == 2 && waits.SequenceEqual(new[] { 1000, 1800 }));
        });
        await check("卡牌节奏：生产随机数128次采样均在要求范围", async () =>
        {
            var waits = new List<int>();
            var pacing = new CardOperationPacing(delay: (ms, _) => { waits.Add(ms); return Task.CompletedTask; });
            for (int i = 0; i < 128; i++) await pacing.WaitAsync(ConstructedActionType.PlayCard, default);
            Need(waits.Count == 128 && waits.All(ms => ms >= 1000 && ms <= 1800) && waits.Distinct().Count() > 1);
        });
        await check("卡牌节奏：出牌攻击技能地标交易特殊操作和换牌均覆盖", async () =>
        {
            var types = new[] { ConstructedActionType.MulliganReplace, ConstructedActionType.PlayCard,
                ConstructedActionType.PlayCardWithTarget, ConstructedActionType.Attack,
                ConstructedActionType.UseHeroPower, ConstructedActionType.UseHeroPowerWithTarget,
                ConstructedActionType.UseLocation, ConstructedActionType.UseLocationWithTarget,
                ConstructedActionType.TradeCard, ConstructedActionType.InternalOption };
            int samples = 0, waits = 0;
            var pacing = new CardOperationPacing((_, _) => { samples++; return 1400; }, (_, _) => { waits++; return Task.CompletedTask; });
            foreach (var type in types) Need(await pacing.WaitAsync(type, default));
            Need(samples == types.Length && waits == types.Length);
        });
        await check("卡牌节奏：目标确认发现选择停止结束回合和无操作不额外等待", async () =>
        {
            var pacing = new CardOperationPacing((_, _) => throw new Exception("Unexpected sampling."),
                (_, _) => throw new Exception("Unexpected wait."));
            foreach (var type in Enum.GetValues<ConstructedActionType>().Where(type => !CardOperationPacing.NeedsDelay(type)))
                Need(!await pacing.WaitAsync(type, default));
        });
        await check("卡牌节奏：真实异步等待不阻塞调用线程且停止立即取消", async () =>
        {
            using var stop = new CancellationTokenSource();
            var pacing = new CardOperationPacing((_, _) => 1800);
            var clock = Stopwatch.StartNew();
            var wait = pacing.WaitAsync(ConstructedActionType.PlayCard, stop.Token);
            Need(!wait.IsCompleted && clock.ElapsedMilliseconds < 500, "等待阻塞了调用线程");
            stop.Cancel(); bool cancelled = false;
            try { await wait; } catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled && clock.ElapsedMilliseconds < 800, "停止未立即取消等待");
        });
        await check("卡牌节奏：真实1000毫秒计时器等待后才完成", async () =>
        {
            var clock = Stopwatch.StartNew();
            Need(await new CardOperationPacing((_, _) => 1000).WaitAsync(ConstructedActionType.PlayCard, default));
            // Timer granularity can vary; the exact requested range is tested above.
            Need(clock.ElapsedMilliseconds >= 950, "真实等待提前完成");
        });
        await check("卡牌节奏：进入等待前已取消不采样不提交", async () =>
        {
            var pacing = new CardOperationPacing((_, _) => throw new Exception("Unexpected sample."));
            bool cancelled = false;
            try { await pacing.WaitAsync(ConstructedActionType.PlayCard, new CancellationToken(true)); }
            catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled);
        });
        await check("卡牌节奏：等待刚结束时取消仍阻止后续执行", async () =>
        {
            using var stop = new CancellationTokenSource();
            var pacing = new CardOperationPacing((_, _) => 1000, (_, _) => { stop.Cancel(); return Task.CompletedTask; });
            bool cancelled = false;
            try { await pacing.WaitAsync(ConstructedActionType.PlayCard, stop.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled);
        });
        await check("卡牌节奏：拒绝越界等待样本而不是静默执行", async () =>
        {
            foreach (int bad in new[] { 0, 999, 1801 })
            {
                bool rejected = false, waited = false;
                var pacing = new CardOperationPacing((_, _) => bad, (_, _) => { waited = true; return Task.CompletedTask; });
                try { await pacing.WaitAsync(ConstructedActionType.PlayCard, default); }
                catch (InvalidOperationException) { rejected = true; }
                Need(rejected && !waited);
            }
        });
        var state = new ConstructedGameState { MatchId = "pacing-fixture", Turn = 2,
            Phase = ConstructedPhase.LocalTurn, Tags = new Dictionary<string, string> { ["unity.optionsPacketId"] = "42" } };
        var play = new ConstructedAction { Type = ConstructedActionType.PlayCard, SourceEntityId = "17" };
        await check("卡牌节奏：局面未变化可以重校验，变化或读失败必须重新决策", () =>
        {
            Need(CardOperationPacing.CanReuseAfterWait(state, state, play));
            var changes = new[] { state with { MatchId = "new-match" }, state with { Turn = 3 },
                state with { Phase = ConstructedPhase.OpponentTurn }, state with { Result = ConstructedGameResult.Win },
                state with { Tags = new Dictionary<string,string> { ["unity.optionsPacketId"] = "43" } },
                state with { Tags = new Dictionary<string,string> { ["unity.optionsPacketId"] = "0" } },
                state with { Tags = new Dictionary<string,string> { ["reason"] = "bridge-failed" } } };
            foreach (var changed in changes) Need(!CardOperationPacing.CanReuseAfterWait(state, changed, play));
            Need(CardOperationPacing.MatchesDecision(new() { MatchId = state.MatchId }, state));
            Need(!CardOperationPacing.MatchesDecision(new() { MatchId = "old-match" }, state));
            return Task.CompletedTask;
        });
        await check("卡牌节奏：盒子建议编号必须仍匹配当前客户端选项", () =>
        {
            foreach (string id in new[] { "41", "0", "garbage" })
                Need(!CardOperationPacing.CanReuseAfterWait(state, state, play with { Parameters = new Dictionary<string,string> { ["netease.optionId"] = id } }));
            Need(CardOperationPacing.CanReuseAfterWait(state, state, play with { Parameters = new Dictionary<string,string> { ["netease.optionId"] = "42" } }));
            return Task.CompletedTask;
        });
        await check("卡牌节奏：只读模式不等待不读取执行管道也不提交", async () =>
        {
            var client = new UnityBridgeClient("HsAuto-no-call-" + Guid.NewGuid().ToString("N"));
            var pacing = new CardOperationPacing((_, _) => throw new Exception("Unexpected sample."));
            var executor = new ConstructedActionExecutor(client, new(client), pacing);
            var result = await executor.ExecuteAsync(new() { Actions = new[] { play }, DryRun = true }, default);
            Need(!result.Attempted);
        });
        await check("卡牌节奏：无可执行动作不等待也不访问管道", async () =>
        {
            var client = new UnityBridgeClient("HsAuto-no-call-" + Guid.NewGuid().ToString("N"));
            var executor = new ConstructedActionExecutor(client, new(client), new((_, _) => throw new Exception("Unexpected sample.")));
            var result = await executor.ExecuteAsync(new() { Actions = new[] { new ConstructedAction { Type = ConstructedActionType.Wait } } }, default);
            Need(!result.Attempted);
        });
        await check("卡牌节奏真实管道：读取→等待→重读→单次出牌", async () =>
        {
            var trace = new List<string>();
            await using var pipe = new PipeFixture((command, _) => command == "battlegroundsStateLite" ? Fixture() : new { accepted = true }, trace);
            var client = new UnityBridgeClient(pipe.Name); var reader = new UnityBridgeConstructedGameStateReader(client);
            var initial = await reader.ReadAsync(default); int waits = 0;
            var pacing = new CardOperationPacing((_, _) => 1000, (ms, _) => { Need(ms == 1000); waits++; trace.Add("wait"); return Task.CompletedTask; });
            var result = await new ConstructedActionExecutor(client, reader, pacing).ExecuteAsync(Decision(initial.State, play), default);
            Need(result.Succeeded, result.Reason); Need(waits == 1);
            Need(trace.SequenceEqual(new[] { "battlegroundsStateLite", "battlegroundsStateLite", "wait", "battlegroundsStateLite", "networkResponse" }), string.Join(",", trace));
        });
        foreach (string changed in new[] { "packet", "phase", "turn", "hand", "broken" })
        {
            await check("卡牌节奏真实管道：等待后" + changed + "变化不提交旧出牌", async () =>
            {
                bool waited = false; var trace = new List<string>();
                await using var pipe = new PipeFixture((command, _) =>
                {
                    Need(command == "battlegroundsStateLite", "过期建议被提交：" + command);
                    if (waited && changed == "broken") return new { }; // reader reports Unknown, never submits
                    return Fixture(packet: waited && changed == "packet" ? 43 : 42,
                        friendly: !(waited && changed == "phase"), turn: waited && changed == "turn" ? 3 : 2,
                        hand: !(waited && changed == "hand"));
                }, trace);
                var client = new UnityBridgeClient(pipe.Name); var reader = new UnityBridgeConstructedGameStateReader(client);
                var initial = await reader.ReadAsync(default);
                var pacing = new CardOperationPacing((_, _) => 1000, (_, _) => { waited = true; return Task.CompletedTask; });
                var result = await new ConstructedActionExecutor(client, reader, pacing).ExecuteAsync(Decision(initial.State, play), default);
                Need(!result.Attempted && waited, result.Reason); Need(trace.Count == 3);
            });
        }
        await check("卡牌节奏真实管道：停止等待后没有补发出牌或重读", async () =>
        {
            var trace = new List<string>();
            await using var pipe = new PipeFixture((_, _) => Fixture(), trace);
            var client = new UnityBridgeClient(pipe.Name); var reader = new UnityBridgeConstructedGameStateReader(client);
            var initial = await reader.ReadAsync(default); using var stop = new CancellationTokenSource();
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pacing = new CardOperationPacing((_, _) => 1800, async (ms, token) => { waiting.SetResult(); await Task.Delay(ms, token); });
            var run = new ConstructedActionExecutor(client, reader, pacing).ExecuteAsync(Decision(initial.State, play), stop.Token);
            await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3)); stop.Cancel(); bool cancelled = false;
            try { await run; } catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled && trace.Count == 2 && trace.All(c => c == "battlegroundsStateLite"));
        });
        await check("卡牌节奏真实管道：指定目标出牌只等待一次不拆散源和目标", async () =>
        {
            var trace = new List<string>();
            await using var pipe = new PipeFixture((command, _) => command == "battlegroundsStateLite" ? Fixture(targeted: true) : new { accepted = true }, trace);
            var client = new UnityBridgeClient(pipe.Name); var reader = new UnityBridgeConstructedGameStateReader(client);
            var initial = await reader.ReadAsync(default); int waits = 0;
            var pacing = new CardOperationPacing((_, _) => 1000, (_, _) => { waits++; return Task.CompletedTask; });
            var targetPlay = play with { Type = ConstructedActionType.PlayCardWithTarget, TargetEntityId = "33" };
            var result = await new ConstructedActionExecutor(client, reader, pacing).ExecuteAsync(Decision(initial.State, targetPlay), default);
            Need(result.Succeeded, result.Reason); Need(waits == 1 && trace.Last() == "constructedTargetedAction");
            Need(trace.Count == 4 && trace.Count(c => c == "constructedTargetedAction") == 1);
        });
        await check("卡牌节奏真实管道：多张起手逐张等待且已选牌不重复点击", async () =>
        {
            var trace = new List<string>();
            await using var pipe = new PipeFixture((command, _) => command == "battlegroundsStateLite" ? Fixture(mulligan: true) : new { accepted = true, choicesSubmitted = true }, trace);
            var client = new UnityBridgeClient(pipe.Name); var reader = new UnityBridgeConstructedGameStateReader(client);
            var initial = await reader.ReadAsync(default); await Task.Delay(850); // the unchanged legacy stability guard
            int waits = 0;
            var pacing = new CardOperationPacing((_, _) => 1000, (_, _) => { waits++; return Task.CompletedTask; });
            var executor = new ConstructedActionExecutor(client, reader, pacing);
            var replacements = new[] { play with { Type = ConstructedActionType.MulliganReplace },
                play with { Type = ConstructedActionType.MulliganReplace, SourceEntityId = "18" } };
            var decision = new ConstructedDecisionEnvelope { MatchId = initial.State.MatchId, Actions = replacements };
            var result = await executor.ExecuteAsync(decision, default);
            Need(result.Succeeded && waits == 2, result.Reason);
            Need(trace.Count(c => c == "selectMulliganCard") == 2);
            result = await executor.ExecuteAsync(decision, default);
            Need(!result.Attempted && waits == 2 && trace.Count(c => c == "selectMulliganCard") == 2);
            result = await executor.ExecuteAsync(decision with { Actions = replacements.Concat(new[] { new ConstructedAction { Type = ConstructedActionType.ConfirmMulligan } }).ToArray() }, default);
            Need(result.Succeeded && waits == 2 && trace.Count(c => c == "confirmMulliganHero") == 1, result.Reason);
        });
        await check("卡牌节奏：日志保持简短中文且不被隐藏", () =>
        {
            var line = "操作节奏：每次独立卡牌操作前随机等待 1.0～1.8 秒。";
            Need(PlayerLog.ForDisplay(line) == line);
            Need(PlayerLog.ForDisplay("[传统执行] 等待期间游戏状态已更新，正在重新获取建议。") == "等待期间游戏状态已更新，正在重新获取建议。");
            return Task.CompletedTask;
        });
    }

    static ConstructedDecisionEnvelope Decision(ConstructedGameState state, ConstructedAction action) =>
        new() { MatchId = state.MatchId, Actions = new[] { action } };

    static object Fixture(int packet = 42, bool friendly = true, int turn = 2, bool hand = true, bool targeted = false, bool mulligan = false)
    {
        object Card(int id, int player, string zone, bool spell = false) => new
        {
            name = "Fixture " + id, path = "/Fixture/" + id, instanceId = id, entity = new
            {
                id = id.ToString(), cardId = "PACING_FIXTURE_" + id, name = "Fixture " + id, zone,
                zonePosition = id == 18 ? 2 : 1, playerId = player, isMinion = !spell, isSpell = spell,
                attack = 1, health = 2, damage = 0, realTimeCost = 1, hasResponse = true, isValidOption = true,
                cardText = spell ? "对一个目标造成1点伤害" : "测试卡牌"
            }
        };
        var objects = new List<object> { Card(33, 2, "PLAY") };
        if (hand) objects.Add(Card(17, 1, "HAND", targeted));
        if (mulligan) objects.Add(Card(18, 1, "HAND"));
        return new { gameTurn = turn, constructedOptionsPacketId = packet,
            hasMulliganConfirmButton = mulligan, isMulliganWaitingForUserInput = mulligan,
            mana = new { ready = 3, total = 3 },
            turnOwner = new { friendlyPlayerId = 1, opposingPlayerId = 2, isFriendlySidePlayerTurn = friendly },
            objects = objects.ToArray(), constructedOptions = Array.Empty<object>() };
    }

    sealed class PipeFixture : IAsyncDisposable
    {
        public string Name { get; } = "HsAuto-pacing-fixture-" + Guid.NewGuid().ToString("N");
        readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(10));
        readonly Task worker;
        public PipeFixture(Func<string, int, object> respond, List<string> trace)
        {
            worker = Task.Run(async () =>
            {
                int reads = 0;
                while (!stop.IsCancellationRequested)
                {
                    using var pipe = SecurePipe.Create(Name);
                    await pipe.WaitForConnectionAsync(stop.Token);
                    using var input = new StreamReader(pipe, leaveOpen: true);
                    await using var output = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    using var request = JsonDocument.Parse((await input.ReadLineAsync(stop.Token))!);
                    var command = request.RootElement.GetProperty("command").GetString()!;
                    trace.Add(command); if (command == "battlegroundsStateLite") reads++;
                    var data = respond(command, reads);
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { ok = true, data }).AsMemory(), stop.Token);
                }
            });
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            try { await worker; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { stop.Dispose(); }
        }
    }
}
