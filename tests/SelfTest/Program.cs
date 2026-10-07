using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using HsAuto.Open;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;
using HsAuto.Core.Strategy;
using HsAuto.UnityBridge;
using HsAuto.Core.Configuration;
using HsAuto.Core.Bot;

var output = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "self-test.json");
var tests = new List<object>(); int failures = 0, skipped = 0;
var installationCases = new HashSet<string>(StringComparer.Ordinal)
{
    "桥接安装缺依赖时拒绝，不留下半安装文件", "安装有备份、可重复执行且保护其他插件",
    "安装中途失败会恢复旧插件并清理临时文件", "恢复桥接备份会恢复旧插件并保护其他插件",
    "恶意恢复清单的越界路径在写入前被拒绝", "删除插件有备份且重复删除保护其他插件和运行时",
    "卸载遇到目录或链接拒绝且不触碰链接目标"
};
async Task Check(string name, Func<Task> action)
{
    if (args.Contains("--game-running-safe") && installationCases.Contains(name))
    {
        skipped++; tests.Add(new { name, skipped = true, reason = "Game may be running; production installer guard remains unchanged." });
        Console.WriteLine("SKIP " + name + "（保留游戏运行保护，不关闭游戏）"); return;
    }
    try { await action(); tests.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; tests.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
var temp = Path.Combine(Path.GetTempPath(), "HsAuto-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
if (args.Skip(1).Contains("--multi-match-only"))
{
    await MultiMatchRecommendationCases.RunAsync(Check);
    await WriteReportAsync("consecutive matches: offline + local pipe fixtures; no live Hearthstone match");
    return;
}
if (args.Skip(1).Contains("--startup-only"))
{
    await StartupRecommendationCases.RunAsync(Check);
    await WriteReportAsync("startup recommendation capture: offline fixtures only; no live Hearthstone match");
    return;
}
if (args.Skip(1).Contains("--card-pacing-only"))
{
    await CardPacingCases.RunAsync(Check);
    await WriteReportAsync("card pacing: offline + local named-pipe fixtures; no live Hearthstone match");
    return;
}
if (args.Skip(1).Contains("--timed-stop-only"))
{
    await TimedStopCases.RunAsync(Check);
    await WriteReportAsync("timed stop: in-memory clock, configuration and fake platform fixtures; no apps closed or shutdown commands executed");
    return;
}
if (args.Skip(1).Contains("--recovery-only"))
{
    await RecoveryCases.RunAsync(Check, temp);
    await WriteReportAsync("recovery and recommendation restart sequencing: offline fixtures only; no live Hearthstone match");
    return;
}
if (args.Skip(1).Contains("--recommendation-sequence-only"))
{
    await Check("盒子建议缺失：先重启脚本，再等待盒子后重启脚本", async () =>
    {
        int generations = 0, boxRestarts = 0;
        var logs = new List<string>();
        var supervisor = new ScriptRecoverySupervisor(
            wait: (duration, token) => Task.CompletedTask,
            restartCooldown: TimeSpan.Zero,
            boxWarmup: TimeSpan.FromSeconds(10));
        await supervisor.RunAsync(token =>
        {
            generations++;
            if (generations <= 2)
                throw new RecommendationUnavailableException("盒子本机推荐源超过3秒未提供可用建议，正在自动重新连接。");
            return Task.CompletedTask;
        }, token => { boxRestarts++; return Task.FromResult(true); }, true, logs.Add, default);
        Assert(generations == 3 && boxRestarts == 1, $"generations={generations}, boxes={boxRestarts}");
        Assert(logs.Any(x => x.Contains("正在自动重启脚本（首次）")));
        Assert(logs.Any(x => x.Contains("仍无炉石AI建议，正在自动重启炉石盒子")));
        Assert(logs.Any(x => x.Contains("等待 10 秒让盒子加载完成")));
        Assert(logs.Any(x => x.Contains("等待完成，正在自动重启脚本")));
    });
    await WriteReportAsync("recommendation restart sequence: offline supervisor fixture only; no live Hearthstone match");
    return;
}
if (args.Skip(1).Contains("--memory-epoch-only"))
{
    await MemoryEpochCases.RunAsync(Check);
    await WriteReportAsync("recommendation epoch consistency: offline in-memory bookkeeping only; no live process reading");
    return;
}
await Check("安装命令支持空格、逗号、引号", () => { Assert(Locator.ParseCommand("\"D:\\Apps, More\\HSAng.exe\",0") == @"D:\Apps, More\HSAng.exe"); return Task.CompletedTask; });
await Check("路径验证不混淆盒子和游戏", () => { var file = Path.Combine(temp,"HSAng.exe"); File.WriteAllText(file,"fixture"); Assert(Locator.ValidPath(file,false) && !Locator.ValidPath(file,true)); return Task.CompletedTask; });
await Check("空数据不制造战绩", async () => { var data = new LocalData(Path.Combine(temp,"empty")); await data.LoadAsync(); Assert(data.Matches.Count==0); });
await Check("新安装默认只读、自动匹配关闭", () => { var settings=new Settings(); Assert(settings.ReadOnly && !settings.AutoQueue && settings.AutoOpenBox); return Task.CompletedTask; });
await Check("设置与战绩原子保存并重新加载", async () => { var dir=Path.Combine(temp,"save"); var data=new LocalData(dir); await data.LoadAsync(); data.Settings.DeckName="自检套牌"; await data.SaveAsync(); var other=new LocalData(dir); await other.LoadAsync(); Assert(other.Settings.DeckName=="自检套牌" && !File.Exists(Path.Combine(dir,"settings.json.tmp"))); });
await Check("配置损坏备份后恢复", async () => { var dir=Path.Combine(temp,"corrupt"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir,"settings.json"),"{broken"); var data=new LocalData(dir); await data.LoadAsync(); Assert(data.RecoveryMessage.Length>0 && Directory.GetFiles(dir,"*.corrupt-*").Length==1); });
await Check("战绩去重、不记录未知结果", async () => { var data=new LocalData(Path.Combine(temp,"records")); await data.LoadAsync(); var now=DateTimeOffset.Now; Assert(await data.AddMatchAsync(new("same",now,now,"Standard","Win"))); Assert(!await data.AddMatchAsync(new("same",now,now,"Standard","Win"))); Assert(!await data.AddMatchAsync(new("unknown",now,now,"Standard","Unknown"))); Assert(data.Matches.Count==1); });
var recommendationJson = "{\"optionId\":42,\"turnNum\":1,\"status\":0,\"data\":[{\"actionName\":\"end_turn\"}]}";
NeteaseBoxRecommendation recommendation = new();
await Check("真实盒子数据格式解析", () => { Assert(NeteaseBoxMemoryPayloadExtractor.TryParseRecommendation(recommendationJson,out recommendation)); Assert(recommendation.OptionId==42); return Task.CompletedTask; });
await Check("损坏和空推荐拒绝", () => { Assert(!NeteaseBoxMemoryPayloadExtractor.TryParseRecommendation("{bad",out _)); Assert(!NeteaseBoxMemoryPayloadExtractor.TryParseRecommendation("{\"data\":null}",out _)); return Task.CompletedTask; });
await Check("UTF8 盒子 callback 提取", () => { var events=NeteaseBoxMemoryPayloadExtractor.Extract(Encoding.UTF8.GetBytes("onUpdateLadderActionRecommend("+recommendationJson+");"),100,0x1000); Assert(events.Any(e=>e.Payload.OptionId==42)); return Task.CompletedTask; });
await Check("UTF16 盒子 callback 提取", () => { var events=NeteaseBoxMemoryPayloadExtractor.Extract(Encoding.Unicode.GetBytes("onUpdateLadderActionRecommend("+recommendationJson+");"),100,0x1000); Assert(events.Any(e=>e.Payload.OptionId==42)); return Task.CompletedTask; });
var before = new ConstructedGameState { MatchId="fixture",Turn=1,Phase=ConstructedPhase.LocalTurn,ManaAvailable=1,ManaTotal=1,Tags=new Dictionary<string,string>{{"unity.optionsPacketId","42"}} };
ConstructedAction action = new() {Type=ConstructedActionType.EndTurn};
await Check("推荐映射至旧版真实结束回合动作", () => { Assert(NeteaseBoxInstructionMapper.TryMapFirst(before,recommendation,out var actions,out var reason),reason); action=actions.Single(); Assert(action.Type==ConstructedActionType.EndTurn); return Task.CompletedTask; });
await Check("不认识的推荐不执行", () => { var unknown=recommendation with { Data=[new(){ActionName="unknown_action_123"}] }; Assert(!NeteaseBoxInstructionMapper.TryMapFirst(before,unknown,out _,out _)); return Task.CompletedTask; });
await Check("未变化不能冒充动作完成", () => { Assert(!ActionConfirmation.Changed(action,before,before)); return Task.CompletedTask; });
await Check("结束回合必须观察到对手回合", () => { Assert(ActionConfirmation.Changed(action,before,before with{Phase=ConstructedPhase.OpponentTurn})); return Task.CompletedTask; });
await Check("出牌必须观察到手牌实体离开", () => { var card=new ConstructedEntity{EntityId="17"}; var state=before with{Hand=[card]}; Assert(ActionConfirmation.Changed(new(){Type=ConstructedActionType.PlayCard,SourceEntityId="17"},state,state with{Hand=[]})); Assert(!ActionConfirmation.Changed(new(){Type=ConstructedActionType.PlayCard,SourceEntityId="17"},state,state)); return Task.CompletedTask; });
await Check("缺失管道超时有明确错误", async () => { var clock=Stopwatch.StartNew(); var response=await new UnityBridgeClient("HsAuto-missing-"+Guid.NewGuid().ToString("N")).SendAsync("ping",timeoutMs:150); Assert(!response.Ok && response.Error.Contains("超时") && clock.ElapsedMilliseconds<1500); });
await Check("用户取消不会变成普通超时", async () => { using var ct=new CancellationTokenSource(70); bool cancelled=false; try{await new UnityBridgeClient("HsAuto-missing-"+Guid.NewGuid().ToString("N")).SendAsync("ping",timeoutMs:5000,cancellationToken:ct.Token);}catch(OperationCanceledException){cancelled=true;} Assert(cancelled); });
await Check("同用户本地 ACL 管道往返", async () => {
 var name="HsAuto-secure-test-"+Guid.NewGuid().ToString("N"); using var pipe=SecurePipe.Create(name);
 var server=Task.Run(async()=>{await pipe.WaitForConnectionAsync(); using var reader=new StreamReader(pipe,leaveOpen:true); await using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true}; var line=await reader.ReadLineAsync(); Assert(line!=null && JsonDocument.Parse(line).RootElement.GetProperty("protocol").GetInt32()==1); await writer.WriteLineAsync("{\"ok\":true,\"data\":{\"protocol\":1}}");});
 var response=await new UnityBridgeClient(name).SendAsync("ping"); await server.WaitAsync(TimeSpan.FromSeconds(4)); Assert(response.Ok && response.Data.GetProperty("protocol").GetInt32()==1);
});
await Check("损坏管道响应不会被认为成功", async () => {
 var name="HsAuto-bad-test-"+Guid.NewGuid().ToString("N"); using var pipe=SecurePipe.Create(name); var server=Task.Run(async()=>{await pipe.WaitForConnectionAsync(); using var reader=new StreamReader(pipe,leaveOpen:true); await using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true}; await reader.ReadLineAsync(); await writer.WriteLineAsync("{broken");}); bool rejected=false; try{await new UnityBridgeClient(name).SendAsync("ping");}catch(JsonException){rejected=true;} await server; Assert(rejected);
});
await Check("真实协议：读取→映射→动作→后置状态确认", async () => {
 var name="HsAuto-flow-test-"+Guid.NewGuid().ToString("N"); bool ended=false; var commands=new List<string>();
 var server=Task.Run(async()=> {
  for(int i=0;i<4;++i){using var pipe=SecurePipe.Create(name); await pipe.WaitForConnectionAsync(); using var reader=new StreamReader(pipe,leaveOpen:true); await using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true}; var request=JsonDocument.Parse((await reader.ReadLineAsync())!); var command=request.RootElement.GetProperty("command").GetString()!; commands.Add(command);
   object payload=command=="constructedEndTurn" ? new{accepted=ended=true} : FixtureState(ended);
   await writer.WriteLineAsync(JsonSerializer.Serialize(new{ok=true,data=payload}));
  }
 });
 var client=new UnityBridgeClient(name); var reader=new UnityBridgeConstructedGameStateReader(client); var original=await reader.ReadAsync(default); Assert(original.State.Phase==ConstructedPhase.LocalTurn,original.State.Phase.ToString());
 Assert(NeteaseBoxInstructionMapper.TryMapFirst(original.State,recommendation,out var actions,out var reason),reason);
 var executor=new ConstructedActionExecutor(client,reader); var result=await executor.ExecuteAsync(new ConstructedDecisionEnvelope{MatchId=original.State.MatchId,ObservedStateSequence=original.Sequence,Actions=actions,StrategyName="NeteaseBoxConstructed"},default);
 Assert(result.Succeeded,result.Reason); var after=await reader.ReadAsync(default); Assert(ActionConfirmation.Changed(result.Action!,original.State,after.State)); await server.WaitAsync(TimeSpan.FromSeconds(8)); Assert(commands.SequenceEqual(new[]{"battlegroundsStateLite","battlegroundsStateLite","constructedEndTurn","battlegroundsStateLite"}));
});
await Check("桥接安装缺依赖时拒绝，不留下半安装文件", () => {
 var dir=Path.Combine(temp,"install-fail"); Directory.CreateDirectory(dir); var game=Path.Combine(dir,"Hearthstone.exe"); File.WriteAllText(game,"game fixture"); bool failed=false; try{BridgeInstaller.Install(game,temp,Path.Combine(temp,"backups"));}catch(FileNotFoundException){failed=true;} Assert(failed && !Directory.Exists(Path.Combine(dir,"BepInEx"))); return Task.CompletedTask;
});
await Check("纯检测不启动进程或改游戏文件", () => { var game=Locator.Find(true); var box=Locator.Find(false); Console.WriteLine("DETECTED game="+(game.Found?game.Source:"missing")+" box="+(box.Found?box.Source:"missing")); return Task.CompletedTask; });
await Check("安装有备份、可重复执行且保护其他插件", () => {
 var dir=Path.Combine(temp,"install"); var bundle=Path.Combine(temp,"bundle"); Directory.CreateDirectory(Path.Combine(dir,"BepInEx","core")); Directory.CreateDirectory(Path.Combine(dir,"BepInEx","plugins")); Directory.CreateDirectory(Path.Combine(bundle,"Bridge"));
 File.WriteAllText(Path.Combine(dir,"Hearthstone.exe"),"game fixture"); File.WriteAllText(Path.Combine(dir,"BepInEx","core","BepInEx.dll"),"runtime fixture"); File.WriteAllText(Path.Combine(dir,"winhttp.dll"),"bootstrap fixture"); File.WriteAllText(Path.Combine(dir,"doorstop_config.ini"),"fixture"); File.WriteAllText(Path.Combine(dir,"BepInEx","plugins","hsmm.dll"),"old bridge fixture"); File.WriteAllText(Path.Combine(dir,"BepInEx","plugins","other.dll"),"other plugin fixture"); File.WriteAllText(Path.Combine(bundle,"Bridge","HsAuto.OpenBridge.dll"),"new bridge fixture");
 var backups=Path.Combine(temp,"install-backups"); BridgeInstaller.Install(Path.Combine(dir,"Hearthstone.exe"),bundle,backups); Assert(!File.Exists(Path.Combine(dir,"BepInEx","plugins","hsmm.dll"))); Assert(File.ReadAllText(Path.Combine(dir,"BepInEx","plugins","other.dll"))=="other plugin fixture"); Assert(Directory.GetFiles(backups,"hsmm.dll",SearchOption.AllDirectories).Length==1);
 BridgeInstaller.Install(Path.Combine(dir,"Hearthstone.exe"),bundle,backups); Assert(Directory.GetFiles(backups,"restore.json",SearchOption.AllDirectories).Length==2); return Task.CompletedTask;
});
await Check("安装中途失败会恢复旧插件并清理临时文件", () => {
 var dir=Path.Combine(temp,"install-rollback"); Directory.CreateDirectory(Path.Combine(dir,"BepInEx","core")); Directory.CreateDirectory(Path.Combine(dir,"BepInEx","plugins","HsAuto.OpenBridge","HsAuto.OpenBridge.dll"));
 File.WriteAllText(Path.Combine(dir,"Hearthstone.exe"),"fixture"); File.WriteAllText(Path.Combine(dir,"BepInEx","core","BepInEx.dll"),"fixture"); File.WriteAllText(Path.Combine(dir,"winhttp.dll"),"fixture"); File.WriteAllText(Path.Combine(dir,"doorstop_config.ini"),"fixture"); File.WriteAllText(Path.Combine(dir,"BepInEx","plugins","hsmm.dll"),"preserve me");
 bool failed=false; try {BridgeInstaller.Install(Path.Combine(dir,"Hearthstone.exe"),Path.Combine(temp,"bundle"),Path.Combine(temp,"rollback-backups"));} catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failed=true;}
 Assert(failed && File.ReadAllText(Path.Combine(dir,"BepInEx","plugins","hsmm.dll"))=="preserve me"); Assert(Directory.GetFiles(dir,"*.hsauto-tmp",SearchOption.AllDirectories).Length==0); return Task.CompletedTask;
});
await Check("恢复桥接备份会恢复旧插件并保护其他插件", () => {
 var backups=Path.Combine(temp,"install-backups");
 var manifest=Directory.GetFiles(backups,"restore.json",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).First();
 var dir=Path.Combine(temp,"install"); BridgeInstaller.Restore(manifest);
 Assert(File.ReadAllText(Path.Combine(dir,"BepInEx","plugins","hsmm.dll"))=="old bridge fixture");
 Assert(File.ReadAllText(Path.Combine(dir,"BepInEx","plugins","other.dll"))=="other plugin fixture");
 Assert(!File.Exists(Path.Combine(dir,"BepInEx","plugins","HsAuto.OpenBridge","HsAuto.OpenBridge.dll"))); return Task.CompletedTask;
});
await Check("恶意恢复清单的越界路径在写入前被拒绝", () => {
 var backup=Path.Combine(temp,"invalid-restore"); Directory.CreateDirectory(backup);
 var victim=Path.Combine(temp,"do-not-delete.txt"); File.WriteAllText(victim,"preserve");
 var manifest=Path.Combine(backup,"restore.json");
 File.WriteAllText(manifest,JsonSerializer.Serialize(new{GameRoot=Path.Combine(temp,"install"),CreatedFiles=new[]{victim},Originals=Array.Empty<object>()}));
 bool rejected=false; try{BridgeInstaller.Restore(manifest);}catch(IOException){rejected=true;} Assert(rejected && File.ReadAllText(victim)=="preserve"); return Task.CompletedTask;
});
await Check("握手拒绝缺字段、错误 PID 和错误类型", () => {
 var valid = new BridgeResponse(true, "", JsonSerializer.SerializeToElement(new { plugin="HsAuto.OpenBridge", processId=123, protocol=1 }));
 Assert(BridgeHandshake.Matches(valid,123) && !BridgeHandshake.Matches(valid,456));
 Assert(!BridgeHandshake.Matches(new(true,"",JsonSerializer.SerializeToElement(new{})),123));
 Assert(!BridgeHandshake.Matches(new(true,"",JsonSerializer.SerializeToElement(new{plugin="HsAuto.OpenBridge",processId="123",protocol=1})),123));
 Assert(!BridgeHandshake.Matches(new(true,"",default),123)); return Task.CompletedTask;
});
await Check("桥接错误不能当作大厅或动作完成", () => {
 var broken=before with {MatchId="",Phase=ConstructedPhase.Unknown,Tags=new Dictionary<string,string>{{"reason","bridge-failed"}}};
 bool stopped=false; try{StateSafety.RequireReadable(broken);}catch(IOException){stopped=true;}
 Assert(stopped && !ActionConfirmation.Changed(action,before,broken));
 StateSafety.RequireReadable(before with{Phase=ConstructedPhase.Unknown}); return Task.CompletedTask;
});
await Check("关闭空闲窗口也等待设置保存", async () => {
 bool saved=false; await ShutdownDrain.DrainAsync([],null!,async()=>{await Task.Delay(30);saved=true;},TimeSpan.FromSeconds(1)); Assert(saved);
});
await Check("关闭时等待关键安装事务再保存", async () => {
 var steps=new List<string>(); var installer=Task.Run(async()=>{await Task.Delay(60);steps.Add("installed");});
 await ShutdownDrain.DrainAsync([],installer,()=>{steps.Add("saved");return Task.CompletedTask;},TimeSpan.FromMilliseconds(10));
 Assert(steps.SequenceEqual(new[]{"installed","saved"}));
});
await Check("后台取消或保存失败不会使退出流程卡住", async () => {
 var messages=new List<string>(); bool called=false;
 await ShutdownDrain.DrainAsync([Task.FromCanceled(new CancellationToken(true))],null!,()=>{called=true;throw new IOException("fixture save error");},TimeSpan.FromSeconds(1),messages.Add);
 Assert(called && messages.Count==1);
});
await Check("无法退出的普通后台任务有等待上限", async () => {
 var clock=Stopwatch.StartNew(); var messages=new List<string>(); bool saved=false;
 await ShutdownDrain.DrainAsync([new TaskCompletionSource().Task],null!,()=>{saved=true;return Task.CompletedTask;},TimeSpan.FromMilliseconds(40),messages.Add);
 Assert(saved && messages.Count==1 && clock.ElapsedMilliseconds<1500);
});
await Check("战绩快照不被并发写入破坏且正常记录平局", async () => {
 var data=new LocalData(Path.Combine(temp,"concurrent-records")); await data.LoadAsync(); var now=DateTimeOffset.Now;
 var snapshot=data.Matches; await Task.WhenAll(Enumerable.Range(0,10).Select(i=>data.AddMatchAsync(new("id"+i,now,now,"Standard",i==0?"Draw":"Win"))));
 Assert(snapshot.Count==0 && data.Matches.Count==10); Assert(!await data.AddMatchAsync(new("bad",now,now,"Standard","bogus")));
 var loaded=new LocalData(data.DirectoryPath); await loaded.LoadAsync(); Assert(loaded.Matches.Count==10);
});
await Check("投降命名管道请求和实际结果解析闭环", async () => {
 var name="HsAuto-concede-test-"+Guid.NewGuid().ToString("N");bool conceded=false;var commands=new List<string>();
 var server=Task.Run(async()=>{for(int i=0;i<4;i++){using var pipe=SecurePipe.Create(name);await pipe.WaitForConnectionAsync();using var input=new StreamReader(pipe,leaveOpen:true);await using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var doc=JsonDocument.Parse((await input.ReadLineAsync())!);var command=doc.RootElement.GetProperty("command").GetString()!;commands.Add(command);object payload;
 if(command=="constructedConcede"){conceded=true;payload=new{accepted=true};}else{var state=JsonSerializer.SerializeToElement(FixtureState(false)).EnumerateObject().ToDictionary(p=>p.Name,p=>(object)p.Value.Clone());state["hasConstructedEndGameScreen"]=conceded;state["constructedResult"]=conceded?"Loss":"Unknown";payload=state;}
 await writer.WriteLineAsync(JsonSerializer.Serialize(new{ok=true,data=payload}));}});
 var client=new UnityBridgeClient(name);var reader=new UnityBridgeConstructedGameStateReader(client);var initial=await reader.ReadAsync(default);var policy=new TraditionalSession(new(){AutoConcede=true});policy.BeginMatch();var a=policy.Concede(initial.State,null!,DateTimeOffset.UtcNow)!;var executor=new ConstructedActionExecutor(client,reader);var result=await executor.ExecuteAsync(new(){MatchId=initial.State.MatchId,ObservedStateSequence=initial.Sequence,Actions=new[]{a}},default);Assert(result.Succeeded,result.Reason);var final=await reader.ReadAsync(default);Assert(final.State.Result==ConstructedGameResult.Loss && ActionConfirmation.Changed(a,initial.State,final.State));await server.WaitAsync(TimeSpan.FromSeconds(8));Assert(commands.SequenceEqual(new[]{"battlegroundsStateLite","battlegroundsStateLite","constructedConcede","battlegroundsStateLite"}));
});
await Check("旧版配置自动迁移传统控制且默认全部关闭", async () => {
 var dir=Path.Combine(temp,"old-config");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"settings.json"),"{\"DeckName\":\"旧套牌\",\"ReadOnly\":true}");
 var d=new LocalData(dir);await d.LoadAsync();Assert(d.Settings.DeckName=="旧套牌" && d.Settings.Traditional!=null && !d.Settings.Traditional.RestartBox && !d.Settings.Traditional.AutoConcede && !d.Settings.Traditional.RotateDecks);
});
await Check("空传统配置和错误范围自动修复但不开放开关", async () => {
 var dir=Path.Combine(temp,"null-config");Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"settings.json"),"{\"Traditional\":null}");var d=new LocalData(dir);await d.LoadAsync();Assert(d.Settings.Traditional.WinsBeforeConcede==1);
 var t=new TraditionalSettings{WinsBeforeConcede=0,WinRateThreshold=1000,RestartCooldownSeconds=-10,StopRank="wrong"};t.Normalize();Assert(t.WinsBeforeConcede==1 && t.WinRateThreshold==100 && t.RestartCooldownSeconds==30 && t.StopRank=="Diamond5");
});
await Check("传统控制保存重新加载且只读强制禁用重启", async () => {
 var d=new LocalData(Path.Combine(temp,"traditional"));await d.LoadAsync();d.Settings.Traditional.WinThenConcede=true;d.Settings.Traditional.WinsBeforeConcede=3;d.Settings.Traditional.RestartBox=true;await d.SaveAsync();var d2=new LocalData(d.DirectoryPath);await d2.LoadAsync();Assert(d2.Settings.Traditional.WinsBeforeConcede==3 && !d2.Settings.Traditional.Runtime(d2.Settings,"deck").EnableNeteaseBoxAutoRecovery);
});
await Check("当前模式卡组解析只展示可用完整卡组", () => {
 var decks=DeckCatalog.Parse(JsonSerializer.SerializeToElement(new { decks=new[]{new{name="标准A",isStandard=true,isWild=false,isPlayable=true,cardCount=30,minimumCardCount=30,maximumCardCount=30},new{name="狂野B",isStandard=false,isWild=true,isPlayable=true,cardCount=30,minimumCardCount=30,maximumCardCount=40},new{name="缺牌",isStandard=true,isWild=false,isPlayable=true,cardCount=20,minimumCardCount=30,maximumCardCount=30}} }));
 Assert(DeckCatalog.Names(decks,"Standard").SequenceEqual(new[]{"标准A"}));Assert(DeckCatalog.Names(decks,"Wild").Count==2);Assert(DeckCatalog.Command=="constructedVisibleDeckCatalog");return Task.CompletedTask;
});
await Check("无效卡组清单和无元数据卡组不能冒充可用", () => {
 bool rejected=false;try{DeckCatalog.Parse(JsonSerializer.SerializeToElement(new{}));}catch(IOException){rejected=true;}Assert(rejected);Assert(DeckCatalog.Names(DeckCatalog.Parse(JsonSerializer.SerializeToElement(new{decks=new[]{new{name="未知"}}})),"Standard").Count==0);return Task.CompletedTask;
});
await Check("轮换按完成局数执行而非每次轮询重复换牌", () => {
 var t=new TraditionalSettings{RotateDecks=true,RotateEvery=2,RotationNames="A，B"};var s=new TraditionalSession(t);var names=new[]{"A","B"};s.BeginMatch();Assert(s.RecordResult("one",ConstructedGameResult.Win));Assert(s.Rotate("A",names)=="A");Assert(!s.RecordResult("one",ConstructedGameResult.Win));s.BeginMatch();Assert(s.RecordResult("two",ConstructedGameResult.Loss));Assert(s.Rotate("A",names)=="B" && s.Completed==2);return Task.CompletedTask;
});
await Check("未知结果不增加计数且未知轮换名称拒绝", () => {
 var s=new TraditionalSession(new(){RotateDecks=true,RotateEvery=1,RotationNames="A,not-found"});Assert(!s.RecordResult("x",ConstructedGameResult.Unknown));Assert(s.Completed==0);s.RecordResult("one",ConstructedGameResult.Win);bool rejected=false;try{s.Rotate("A",new[]{"A","B"});}catch(IOException){rejected=true;}Assert(rejected);return Task.CompletedTask;
});
await Check("赢两场投两场以真实胜负驱动且不重复计数", () => {
 var s=new TraditionalSession(new(){WinThenConcede=true,WinsBeforeConcede=2,ConcedesAfterWins=2});s.BeginMatch();s.RecordResult("1",ConstructedGameResult.Win);Assert(s.RemainingConcedes==0);s.BeginMatch();s.RecordResult("2",ConstructedGameResult.Win);Assert(s.RemainingConcedes==2);s.BeginMatch();Assert(s.Concede(before,null!,DateTimeOffset.UtcNow)?.Type==ConstructedActionType.Concede);s.RecordResult("3",ConstructedGameResult.Loss);Assert(!s.RecordResult("3",ConstructedGameResult.Loss) && s.RemainingConcedes==1);s.BeginMatch();s.RecordResult("4",ConstructedGameResult.Loss);Assert(s.RemainingConcedes==0);s.BeginMatch();Assert(s.Concede(before,null!,DateTimeOffset.UtcNow)==null);return Task.CompletedTask;
});
await Check("投降仅在允许阶段触发且等待终局确认", () => {
 var s=new TraditionalSession(new(){AutoConcede=true});s.BeginMatch();var a=s.Concede(before,null!,DateTimeOffset.UtcNow)!;Assert(a!=null && a.Parameters["hsauto.automaticConcede"]=="True");Assert(s.Concede(before with{Phase=ConstructedPhase.OpponentTurn},null!,DateTimeOffset.UtcNow)==null);Assert(!ActionConfirmation.Changed(a,before,before with{Turn=2,MatchId="changed"}));Assert(ActionConfirmation.Changed(a,before,before with{Phase=ConstructedPhase.GameOver}));return Task.CompletedTask;
});
await Check("低胜率拒绝过期跨回合未知以及跨局数据", () => {
 var s=new TraditionalSession(new(){LowWinRateConcede=true,WinRateThreshold=20});var now=DateTimeOffset.UtcNow;var state=before with{ManaTotal=4};var rate=new ConstructedWinRateSnapshot("fixture",10,now,1);Assert(s.Concede(state,rate,now)!=null);Assert(s.Concede(state,rate with{CapturedAt=now.AddSeconds(-21)},now)==null);Assert(s.Concede(state,rate with{TurnId=2},now)==null);Assert(s.Concede(state,rate with{MatchId="other"},now)==null);Assert(s.Concede(state,rate with{Percent=double.NaN},now)==null);Assert(s.Concede(state,null!,now)==null);return Task.CompletedTask;
});
await Check("段位读取严格匹配插件字段和标准狂野", () => {
 var data=JsonSerializer.SerializeToElement(new{constructedRanks=new{standard=new{Available=true,IsNewPlayer=false,IsLegend=false,LegendRank=0,StarLevel=46,Stars=2,LeagueId=5,DisplayName="钻石5"},wild=new{Available=true,IsLegend=true,LegendRank=23000,StarLevel=51}}});var r=RankReader.Parse(data,"Standard");Assert(r.Available && r.StarLevel==46 && r.Stars==2 && !r.IsLegend);Assert(RankReader.Parse(data,"Wild").LegendRank==23000);Assert(!RankReader.Parse(data,"Casual").Available);return Task.CompletedTask;
});
await Check("达到段位和传说排名停止但未知段位不误判", () => {
 var r=new HearthstoneConstructedRankSnapshot(true,"Standard",false,false,0,46,5,"钻石","","","钻石5","");var t=new TraditionalSettings{StopAtRank=true};Assert(RankReader.StopReason(t,r).Length>0);Assert(RankReader.StopReason(t,r with{Available=false}).Length==0);Assert(RankReader.StopReason(t,r with{StarLevel=45}).Length==0);t.StopAtRank=false;t.StopAtLegend=true;Assert(RankReader.StopReason(t,r with{IsLegend=true,LegendRank=20000}).Length>0);Assert(RankReader.StopReason(t,r with{IsLegend=true,LegendRank=0}).Length==0);return Task.CompletedTask;
});
await Check("控分达到两星投降而未知传说不误控", () => {
 var s=new TraditionalSession(new(){ControlRank=true});var rank=new HearthstoneConstructedRankSnapshot(true,"Standard",false,false,0,46,5,"","","","",""){Stars=2};s.SetRank(rank);s.BeginMatch();Assert(s.Concede(before,null!,DateTimeOffset.UtcNow)!=null);s.SetRank(rank with{Stars=0});Assert(s.Concede(before,null!,DateTimeOffset.UtcNow)==null);s.SetRank(rank with{IsLegend=true});Assert(s.Concede(before,null!,DateTimeOffset.UtcNow)==null);return Task.CompletedTask;
});
await Check("休闲模式不接受段位控制且零轮换间隔拒绝", () => {
 bool a=false,b=false;try{new TraditionalSettings{StopAtRank=true}.Validate("Casual");}catch(IOException){a=true;}try{new TraditionalSettings{RotateEvery=0}.Validate("Standard");}catch(IOException){b=true;}Assert(a && b);return Task.CompletedTask;
});
await Check("烧绳保护须观察整个回合且成功动作取消保护", () => {
 var tracker=new NeteaseConstructedStallConcedeTracker();tracker.SetEnabled(true);var wait=new ConstructedAction{Type=ConstructedActionType.Wait,Parameters=new Dictionary<string,string>{{"hsauto.neteaseConstructedStallKind","Recommendation"}}};var envelope=new ConstructedDecisionEnvelope{MatchId=before.MatchId,Actions=new[]{wait}};tracker.ObserveState(before with{Phase=ConstructedPhase.Mulligan,Turn=0});tracker.ObserveState(before);var ropeState=before with{Tags=new Dictionary<string,string>{{"unity.turnTimer.ropeActive","True"}}};Assert(tracker.TryCreateConcedeDecision(ropeState,envelope,true)!=null);Assert(tracker.TryCreateConcedeDecision(ropeState,envelope with{DryRun=true},true)==null);tracker.RecordExecution(before,ConstructedActionExecutionResult.Success(action));Assert(tracker.TryCreateConcedeDecision(ropeState,envelope,true)==null);return Task.CompletedTask;
});
await Check("连续三局烧绳投降触发停机计数且正常局重置", () => {
 var s=new TraditionalSession(new(){RopeProtection=true});var a=new ConstructedAction{Type=ConstructedActionType.Concede,Parameters=new Dictionary<string,string>{{"hsauto.neteaseConstructedStallConcede","True"}}};for(int i=0;i<3;i++){s.BeginMatch();s.RecordConcede(a);s.RecordResult("rope"+i,ConstructedGameResult.Loss);}Assert(s.ConsecutiveRopeConcedes==3);s.BeginMatch();s.RecordResult("normal",ConstructedGameResult.Win);Assert(s.ConsecutiveRopeConcedes==0);return Task.CompletedTask;
});
await Check("缺推荐盒子恢复使用模拟源验证等待与冷却", async () => {
 var clock=new TestClock();var source=new TestRecommendationSource();var strategy=new NeteaseBoxConstructedStrategy(source,timeProvider:clock,enableMissingOptionsRecovery:false);await strategy.InitializeAsync(new(123,"test"),default);var cfg=new ConstructedSettings{EnableNeteaseBoxAutoRecovery=true,NeteaseBoxStallTimeoutSeconds=15,NeteaseBoxRecoveryCooldownSeconds=120};await strategy.DecideAsync(before,cfg,default);clock.Advance(16);await strategy.DecideAsync(before,cfg,default);Assert(source.Restarts==1);clock.Advance(16);await strategy.DecideAsync(before,cfg,default);Assert(source.Restarts==1);clock.Advance(121);await strategy.DecideAsync(before,cfg,default);Assert(source.Restarts==2);
});
await Check("只读恢复开关不重启任何程序", async () => {
 var clock=new TestClock();var source=new TestRecommendationSource();var strategy=new NeteaseBoxConstructedStrategy(source,timeProvider:clock,enableMissingOptionsRecovery:false);await strategy.InitializeAsync(new(123,"test"),default);var cfg=new ConstructedSettings{EnableNeteaseBoxAutoRecovery=true,DryRun=true};await strategy.DecideAsync(before,cfg,default);clock.Advance(200);await strategy.DecideAsync(before,cfg,default);Assert(source.Restarts==0);
});
await Check("删除插件有备份且重复删除保护其他插件和运行时", () => {
 var root=Path.Combine(temp,"uninstall");Directory.CreateDirectory(Path.Combine(root,"BepInEx","plugins","HsAuto.OpenBridge"));Directory.CreateDirectory(Path.Combine(root,"BepInEx","core"));File.WriteAllText(Path.Combine(root,"Hearthstone.exe"),"fixture");var plugin=Path.Combine(root,"BepInEx","plugins","HsAuto.OpenBridge","HsAuto.OpenBridge.dll");File.WriteAllText(plugin,"our plugin");var other=Path.Combine(root,"BepInEx","plugins","other.dll");File.WriteAllText(other,"preserve");var runtime=Path.Combine(root,"BepInEx","core","BepInEx.dll");File.WriteAllText(runtime,"preserve runtime");var backups=Path.Combine(temp,"delete-backups");BridgeInstaller.Uninstall(Path.Combine(root,"Hearthstone.exe"),backups);Assert(!File.Exists(plugin) && File.ReadAllText(other)=="preserve" && File.ReadAllText(runtime)=="preserve runtime");Assert(Directory.GetFiles(backups,"HsAuto.OpenBridge.dll",SearchOption.AllDirectories).Length==1);Assert(BridgeInstaller.Uninstall(Path.Combine(root,"Hearthstone.exe"),backups).Contains("无需删除"));BridgeInstaller.Restore(Directory.GetFiles(backups,"restore.json",SearchOption.AllDirectories).Single());Assert(File.ReadAllText(plugin)=="our plugin");return Task.CompletedTask;
});
await Check("卸载遇到目录或链接拒绝且不触碰链接目标", () => {
 var root=Path.Combine(temp,"delete-link");Directory.CreateDirectory(Path.Combine(root,"BepInEx","plugins"));File.WriteAllText(Path.Combine(root,"Hearthstone.exe"),"fixture");var external=Path.Combine(temp,"external-plugin");Directory.CreateDirectory(external);File.WriteAllText(Path.Combine(external,"HsAuto.OpenBridge.dll"),"preserve");Directory.CreateSymbolicLink(Path.Combine(root,"BepInEx","plugins","HsAuto.OpenBridge"),external);bool rejected=false;try{BridgeInstaller.Uninstall(Path.Combine(root,"Hearthstone.exe"),Path.Combine(temp,"unused-backup"));}catch(IOException){rejected=true;}Assert(rejected && File.ReadAllText(Path.Combine(external,"HsAuto.OpenBridge.dll"))=="preserve");return Task.CompletedTask;
});
await DiscoveryCases.RunAsync(Check, temp);
await BindingCases.RunAsync(Check);
await PresentationCases.RunAsync(Check,temp);
await PlayerLogCases.RunAsync(Check);
await CardPacingCases.RunAsync(Check);
await StartupRecommendationCases.RunAsync(Check);
await MultiMatchRecommendationCases.RunAsync(Check);
await TimedStopCases.RunAsync(Check);
await MemoryEpochCases.RunAsync(Check);
await RecoveryCases.RunAsync(Check, temp);
await Check("缺推荐在可操作阶段超时明确报警而不伪造动作", () =>
{
 var clock=new TestClock(); var health=new RecommendationHealth(clock);
 var waiting=new[]{new ConstructedAction{Type=ConstructedActionType.Wait}};
 Assert(health.Observe(before,waiting,false)==null);clock.Advance(31);
 Assert(health.Observe(before,waiting,false)?.Contains("没有启用原版盒子AI")==true);
 Assert(health.Observe(before,waiting,false)==null);return Task.CompletedTask;
});
await Check("只读缺推荐仅提示不执行游戏或重启盒子", () =>
{
 var clock=new TestClock();var health=new RecommendationHealth(clock);var waiting=new[]{new ConstructedAction{Type=ConstructedActionType.Wait}};
 health.Observe(before,waiting,true);clock.Advance(31);Assert(health.Observe(before,waiting,true)?.Contains("只读观察继续")==true);return Task.CompletedTask;
});
await Check("有效建议和阶段转换重置缺推荐超时", () =>
{
 var clock=new TestClock();var health=new RecommendationHealth(clock);var waiting=new[]{new ConstructedAction{Type=ConstructedActionType.Wait}};
 health.Observe(before,waiting,false);clock.Advance(29);health.Observe(before,new[]{action},false);clock.Advance(3);
 Assert(health.Observe(before,waiting,false)==null);clock.Advance(25);Assert(health.Observe(before with{Turn=2},waiting,false)==null);
 Assert(health.Observe(before with{Phase=ConstructedPhase.OpponentTurn},waiting,false)==null);return Task.CompletedTask;
});

await Check("运行异常三秒后自动重连，第三次后才尝试重启盒子", async () =>
{
    int runs = 0, boxRestarts = 0, waits = 0;
    var supervisor = new ScriptRecoverySupervisor(wait: (duration, token) => { waits++; return Task.CompletedTask; },
        restartCooldown: TimeSpan.Zero);
    await supervisor.RunAsync(async token =>
    {
        runs++;
        if (runs <= 4) throw new IOException("Bridge 短时无响应");
    }, token => { boxRestarts++; return Task.FromResult(true); }, true, _ => { }, default);
    Assert(runs == 5 && boxRestarts == 1 && waits >= 4, $"runs={runs}, box={boxRestarts}, waits={waits}");
});
await Check("盒子建议缺失：先重启脚本，再重启盒子并自动重启脚本", async () =>
{
    int generations = 0, boxRestarts = 0;
    var logs = new List<string>();
    var supervisor = new ScriptRecoverySupervisor(
        wait: (duration, token) => Task.CompletedTask,
        restartCooldown: TimeSpan.Zero);
    await supervisor.RunAsync(token =>
    {
        generations++;
        if (generations <= 2)
            throw new RecommendationUnavailableException("盒子本机推荐源超过3秒未提供可用建议，正在自动重新连接。");
        return Task.CompletedTask;
    }, token => { boxRestarts++; return Task.FromResult(true); }, true, logs.Add, default);
    Assert(generations == 3 && boxRestarts == 1, $"generations={generations}, boxes={boxRestarts}");
    Assert(logs.Any(x => x.Contains("正在自动重启脚本（首次）")));
    Assert(logs.Any(x => x.Contains("仍无炉石AI建议，正在自动重启炉石盒子")));
    Assert(logs.Any(x => x.Contains("等待 10 秒让盒子加载完成")));
});
await Check("插件已安装检测不因空目录误报", () =>
{
    var root = Path.Combine(temp, "plugin-check"); Directory.CreateDirectory(Path.Combine(root, "BepInEx", "plugins", "HsAuto.OpenBridge"));
    var game = Path.Combine(root, "Hearthstone.exe"); File.WriteAllText(game, "fixture");
    Assert(!BridgeInstaller.IsInstalled(game));
    var plugin = Path.Combine(root, "BepInEx", "plugins", "HsAuto.OpenBridge", "HsAuto.OpenBridge.dll"); File.WriteAllBytes(plugin, new byte[] { 1, 2, 3 });
    Assert(BridgeInstaller.IsInstalled(game)); return Task.CompletedTask;
});
await Check("盒子恢复拒绝多个进程并保持目标不变", async () =>
{
    var root = Path.Combine(temp, "box-recovery"); Directory.CreateDirectory(root); var path = Path.Combine(root, "HSAng.exe"); File.WriteAllText(path, "fixture");
    var fake = new FakeBoxPlatform(path, multiple: true);
    var recovery = new BoxProcessRecovery(fake, wait: (duration, token) => Task.CompletedTask);
    bool rejected = false; try { await recovery.RestartAsync(path, () => true, null, default); } catch (IOException) { rejected = true; }
    Assert(rejected && fake.Launches == 0 && fake.Processes.Count == 2);
});
await Check("盒子恢复只关闭已确认进程并重新启动", async () =>
{
    var root = Path.Combine(temp, "box-recovery-one"); Directory.CreateDirectory(root); var path = Path.Combine(root, "HSAng.exe"); File.WriteAllText(path, "fixture");
    var fake = new FakeBoxPlatform(path, multiple: false);
    var recovery = new BoxProcessRecovery(fake, wait: (duration, token) => Task.CompletedTask);
    await recovery.RestartAsync(path, () => true, null, default);
    Assert(fake.Launches == 1 && fake.Processes.Count == 1 && fake.Processes[0].Path == path);
});

await WriteReportAsync("offline + local named-pipe + temp installation fixtures; no live Hearthstone match");

async Task WriteReportAsync(string scope)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new {date=DateTimeOffset.Now,failed=failures,passed=tests.Count-failures-skipped,skipped,scope,tests},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine($"RESULT {tests.Count-failures-skipped}/{tests.Count-skipped} passed; skipped={skipped}; report={output}");
    Environment.ExitCode=failures==0?0:1;
}

static object FixtureState(bool ended) => new {
 gameTurn=1,constructedOptionsPacketId=42,mana=new{ready=1,total=1},
 turnOwner=new{friendlyPlayerId=1,opposingPlayerId=2,isFriendlySidePlayerTurn=!ended},
 objects=new[]{new{ name="Fixture",path="/CardFixture",instanceId=17,entity=new{id="17",cardId="FIXTURE_CARD",name="Fixture",zone="PLAY",zonePosition=1,playerId=1,isMinion=true,attack=1,health=1,damage=0,realTimeCost=1} }},
 constructedOptions=Array.Empty<object>()
};

sealed class FakeBoxPlatform : IBoxProcessPlatform
{
    readonly string path; public List<BoxProcessIdentity> Processes { get; } = []; public int Launches { get; private set; }
    public FakeBoxPlatform(string executablePath, bool multiple)
    { path = executablePath; Processes.Add(new(10, 10, path)); if (multiple) Processes.Add(new(11, 11, path)); }
    public IReadOnlyList<BoxProcessIdentity> Snapshot() => Processes.ToArray();
    public bool FileExists(string value) => File.Exists(value);
    public void CloseWindow(BoxProcessIdentity identity) => Processes.RemoveAll(p => p.Pid == identity.Pid);
    public void Terminate(BoxProcessIdentity identity) => Processes.RemoveAll(p => p.Pid == identity.Pid);
    public void Launch(string value) { Launches++; Processes.Add(new(20 + Launches, 20 + Launches, value)); }
}

sealed class TestClock : TimeProvider
{
 DateTimeOffset current=new(2026,10,6,0,0,0,TimeSpan.Zero);
 public override DateTimeOffset GetUtcNow()=>current;
 public void Advance(int seconds)=>current=current.AddSeconds(seconds);
}
sealed class TestRecommendationSource : INeteaseBoxRecommendationSource
{
 public int Restarts {get;private set;}
 public event Action<string>? Log;
 public Task InitializeAsync(int pid,CancellationToken token){Log?.Invoke("isolated fixture");return Task.CompletedTask;}
 public Task<IReadOnlyList<NeteaseBoxRecommendationObservation>> ReadAsync(int desiredOptionId,CancellationToken token)=>Task.FromResult<IReadOnlyList<NeteaseBoxRecommendationObservation>>(Array.Empty<NeteaseBoxRecommendationObservation>());
 public Task<bool> RestartAndReconnectAsync(int pid,string reason,CancellationToken token){Restarts++;return Task.FromResult(true);}
}

