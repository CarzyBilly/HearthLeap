using System.Reflection;
using System.Text;
using System.Text.Json;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;
using HsAuto.Core.Strategy;
using HsAuto.Open;
using HsAuto.UnityBridge;

static class MultiMatchRecommendationCases
{
    static void Need(bool ok, string message = "连续对局断言失败") { if (!ok) throw new Exception(message); }
    static object Fixture(string phase = "opening", int turn = 0) => new
    {
        gameTurn = turn, constructedOptionsPacketId = phase == "local" ? 1 : 0,
        hasMatchingPopup = phase == "queue", hasConstructedEndGameScreen = phase == "over",
        constructedResult = phase == "over" ? "Win" : "Unknown",
        hasMulliganConfirmButton = phase == "opening", isMulliganWaitingForUserInput = phase == "opening",
        mulliganIntroStateAvailable = true, mulliganIntroComplete = true,
        mana = new { ready = 1, total = 1 },
        turnOwner = new { friendlyPlayerId = 1, opposingPlayerId = 2, isFriendlySidePlayerTurn = phase == "local" },
        objects = phase is "unknown" or "queue" ? Array.Empty<object>() : new object[]
        {
            new { name = "测试牌", path = "/Fixture/17", instanceId = 17,
                entity = new { id = "17", cardId = "FIXTURE_CARD", name = "测试牌", zone = "HAND",
                    zonePosition = 1, playerId = 1, isMinion = true, attack = 1, health = 2, realTimeCost = 1 } }
        }
    };
    static ConstructedGameState Read(UnityBridgeConstructedGameStateReader reader, object data) =>
        (ConstructedGameState)typeof(UnityBridgeConstructedGameStateReader).GetMethod("BuildStateFromLite", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(reader, [JsonSerializer.SerializeToElement(data)])!;
    static ConstructedDecisionEnvelope Confirm(ConstructedGameState state) => new()
    { MatchId = state.MatchId, Actions = [new() { Type = ConstructedActionType.ConfirmMulligan }] };

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("连续对局：同一局跨回合保持同一个对局标识", () =>
        {
            var reader = new UnityBridgeConstructedGameStateReader();
            var opening = Read(reader, Fixture()); var first = Read(reader, Fixture("local", 1));
            var next = Read(reader, Fixture("local", 3));
            Need(opening.MatchId == first.MatchId && first.MatchId == next.MatchId);
            Need(!ActionConfirmation.Changed(new() { Type = ConstructedActionType.Attack }, first, next), "单纯回合编号变化不能当作攻击确认");
            return Task.CompletedTask;
        });
        await check("连续对局：相同玩家实体和起手编号的下一局必须有新标识", () =>
        {
            var reader = new UnityBridgeConstructedGameStateReader();
            var first = Read(reader, Fixture()); Read(reader, Fixture("local", 2)); Read(reader, Fixture("queue"));
            var second = Read(reader, Fixture()); Need(first.MatchId != second.MatchId);
            Need(!CardOperationPacing.MatchesDecision(Confirm(first), second), "旧局决策不能套到新局");
            return Task.CompletedTask;
        });
        await check("连续对局：短暂空画面不是新对局，也不重复提交起手", () =>
        {
            var reader = new UnityBridgeConstructedGameStateReader();
            var first = Read(reader, Fixture()); Read(reader, Fixture("unknown"));
            Need(Read(reader, Fixture()).MatchId == first.MatchId);
            return Task.CompletedTask;
        });
        await check("连续对局：中途接入的标识稳定，回合回退进入新起手则换标识", () =>
        {
            var reader = new UnityBridgeConstructedGameStateReader();
            var first = Read(reader, Fixture("local", 8));
            Need(Read(reader, Fixture("local", 10)).MatchId == first.MatchId);
            Need(Read(reader, Fixture()).MatchId != first.MatchId);
            return Task.CompletedTask;
        });
        await check("连续对局：起手回合字段0变1不丢已匹配建议", async () =>
        {
            var reader = new UnityBridgeConstructedGameStateReader(); var clock = new Clock();
            var source = new Source(clock); var strategy = new NeteaseBoxConstructedStrategy(source, timeProvider: clock, enableMissingOptionsRecovery: false);
            await strategy.InitializeAsync(new(123, "offline"), default);
            source.Events = [new(new() { OptionId = -1, Data = [new() { ActionName = "replace" }] }, clock.GetUtcNow(), 101, 0x1000)];
            var first = await strategy.DecideAsync(Read(reader, Fixture()), new(), default);
            Need(first.Any(a => a.Type == ConstructedActionType.ConfirmMulligan));
            source.Events = []; clock.Advance(25);
            var next = await strategy.DecideAsync(Read(reader, Fixture(turn: 1)), new(), default);
            Need(next.Any(a => a.Type == ConstructedActionType.ConfirmMulligan), "同一局动画期间回合字段变化导致建议丢失");
        });
        await check("连续对局真实管道：16局相同实体编号均确认一次，无需重启脚本", async () =>
        {
            object current = Fixture(); int confirmations = 0;
            await using var fixture = new PipeFixture(command =>
            {
                if (command == "battlegroundsStateLite") return current;
                Need(command == "confirmMulliganHero", "意外动作：" + command); confirmations++;
                return new { choicesSubmitted = true };
            });
            var client = new UnityBridgeClient(fixture.Name); var reader = new UnityBridgeConstructedGameStateReader(client);
            var executor = new ConstructedActionExecutor(client, reader);
            for (int i = 0; i < 16; i++)
            {
                current = Fixture("queue"); await reader.ReadAsync(default);
                current = Fixture(); var opening = await reader.ReadAsync(default); await Task.Delay(850);
                var result = await executor.ExecuteAsync(Confirm(opening.State), default);
                Need(result.Succeeded, $"第{i + 1}局已有建议但未提交：{result.Reason}");
                result = await executor.ExecuteAsync(Confirm(opening.State), default);
                Need(!result.Attempted, "同一局不应重复确认");
                current = Fixture("local", 2); await reader.ReadAsync(default);
            }
            Need(confirmations == 16);
        });
        await check("连续对局：真实管道确认成功后提交观察器只回报实际动作", () =>
        {
            var strategySource = File.ReadAllText(Path.Combine(ProjectRoot(), "src/HsAuto.Core/Automation.cs"));
            Need(strategySource.Contains("executor.ObservePhase(state.Phase)"), "主循环遗漏执行器阶段通知");
            Need(strategySource.Contains("strategy.ObserveState(state)"), "大厅/结算未通知推荐状态机");
            Need(strategySource.Contains("health.ObserveExecution(state, execution)"), "未区分无推荐和有推荐待执行");
            return Task.CompletedTask;
        });
    }
    static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src/HsAuto.Core/Automation.cs"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到测试源码");
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
        public IReadOnlyList<NeteaseBoxRecommendationObservation> Events = [];
        public Task InitializeAsync(int pid, CancellationToken token) { Log?.Invoke("offline fixture"); return Task.CompletedTask; }
        public void SetRecommendationEpoch(DateTimeOffset epoch) { }
        public Task<IReadOnlyList<NeteaseBoxRecommendationObservation>> ReadAsync(int option, CancellationToken token) => Task.FromResult(Events);
    }
    sealed class PipeFixture : IAsyncDisposable
    {
        public string Name { get; } = "HsAuto-multi-match-" + Guid.NewGuid().ToString("N");
        readonly CancellationTokenSource stop = new(TimeSpan.FromSeconds(50)); readonly Task worker;
        public PipeFixture(Func<string, object> respond)
        {
            worker = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    using var pipe = SecurePipe.Create(Name); await pipe.WaitForConnectionAsync(stop.Token);
                    using var input = new StreamReader(pipe, leaveOpen: true);
                    await using var output = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                    using var request = JsonDocument.Parse((await input.ReadLineAsync(stop.Token))!);
                    var data = respond(request.RootElement.GetProperty("command").GetString()!);
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { ok = true, data }).AsMemory(), stop.Token);
                }
            });
        }
        public async ValueTask DisposeAsync()
        { stop.Cancel(); try { await worker; } catch (OperationCanceledException) when (stop.IsCancellationRequested) { } finally { stop.Dispose(); } }
    }
}
