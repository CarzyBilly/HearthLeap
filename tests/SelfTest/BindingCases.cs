using System.Diagnostics;
using System.Globalization;
using HsAuto.Core.Automation;

public static class BindingCases
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-06T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly NeteaseBoxProcessSession Box = new(10, Now.AddMinutes(-6).UtcTicks, @"X:\fixture\HSAng.exe");
    private static readonly NeteaseBoxHearthstoneProcess Target = new(20, Now.AddMinutes(-2).UtcTicks, true);
    private static readonly NeteaseBoxHelperEvidence Current = new(NeteaseBoxHelperEvidenceKind.Valid, 20, Now.AddMinutes(-1).UtcTicks);

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("绑定：当前 PID、进程时间、助手时间和覆盖层共同确认", () =>
        {
            var result = Assess(Current);
            Need(result.Verdict == NeteaseBoxBindingVerdict.Verified && !result.Reused && result.Session.TargetStartTimeUtcTicks == Target.StartTimeUtcTicks);
            Need(Assess(Current, target: Target with { HasOverlay = false }).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：同一会话重新开始复用，缺失日志不制造初次成功", () =>
        {
            var proof = Assess(Current).Session;
            Need(Assess(NeteaseBoxHelperEvidence.Missing).Verdict == NeteaseBoxBindingVerdict.Waiting);
            var reused = Assess(NeteaseBoxHelperEvidence.Missing, proof);
            Need(reused.Verdict == NeteaseBoxBindingVerdict.Verified && reused.Reused);
            Need(Assess(Current).Verdict == NeteaseBoxBindingVerdict.Verified); // No app cache needed while the latest session record remains valid.
            return Task.CompletedTask;
        });
        await check("绑定：历史死 PID 不冒充活动冲突或已确认绑定", () =>
        {
            var historical = new NeteaseBoxHelperEvidence(NeteaseBoxHelperEvidenceKind.Valid, 19, Now.AddMinutes(-3).UtcTicks);
            var result = Assess(historical);
            Need(result.Verdict == NeteaseBoxBindingVerdict.Waiting && result.Reason.Contains("历史记录"));
            return Task.CompletedTask;
        });
        await check("绑定：复用 PID 的旧助手时间不能认证新游戏实例", () =>
        {
            var old = Current with { EventTimeUtcTicks = Now.AddMinutes(-3).UtcTicks };
            Need(Assess(old).Verdict == NeteaseBoxBindingVerdict.Waiting);
            var proof = Assess(Current).Session;
            var replaced = Target with { StartTimeUtcTicks = Now.AddSeconds(-20).UtcTicks };
            Need(Assess(Current, proof, target: replaced).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(NeteaseBoxHelperEvidence.Missing, proof, target: replaced).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：盒子 PID、启动时间和安装路径变化均使旧证明失效", () =>
        {
            var proof = Assess(Current).Session;
            foreach (var changed in new[] { Box with { ProcessId = 11 }, Box with { StartTimeUtcTicks = Now.AddSeconds(-15).UtcTicks }, Box with { ExecutablePath = @"X:\other\HSAng.exe" } })
                Need(Assess(NeteaseBoxHelperEvidence.Missing, proof, box: changed).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(Current, proof, box: Box with { StartTimeUtcTicks = Now.AddSeconds(-15).UtcTicks }).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：其他活动客户端冲突优先，不能被缓存掩盖", () =>
        {
            var other = new NeteaseBoxHearthstoneProcess(30, Now.AddMinutes(-4).UtcTicks, true);
            var log = new NeteaseBoxHelperEvidence(NeteaseBoxHelperEvidenceKind.Valid, 30, Now.AddMinutes(-3).UtcTicks);
            foreach (var proof in new NeteaseBoxVerifiedSession?[] { null, Assess(Current).Session })
                Need(Assess(log, proof, other: other).Verdict == NeteaseBoxBindingVerdict.Conflict);
            Need(Assess(log, other: other with { StartTimeUtcTicks = 0 }).Verdict == NeteaseBoxBindingVerdict.Conflict);
            return Task.CompletedTask;
        });
        await check("绑定：其他客户端 PID 重用的旧事件不是活动冲突", () =>
        {
            var reusedPid = new NeteaseBoxHearthstoneProcess(30, Now.AddSeconds(-15).UtcTicks, true);
            var log = new NeteaseBoxHelperEvidence(NeteaseBoxHelperEvidenceKind.Valid, 30, Now.AddMinutes(-3).UtcTicks);
            Need(Assess(log, other: reusedPid).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：多开缺乏当前助手证据时不能凭旧缓存猜测账号", () =>
        {
            var other = new NeteaseBoxHearthstoneProcess(30, Now.AddMinutes(-4).UtcTicks, true);
            var proof = Assess(Current).Session;
            Need(Assess(NeteaseBoxHelperEvidence.Missing, proof, other: other).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(Current, proof, other: other).Verdict == NeteaseBoxBindingVerdict.Verified);
            return Task.CompletedTask;
        });
        await check("绑定：更晚的死客户端绑定事件也不能复活旧缓存", () =>
        {
            var proof = Assess(Current).Session;
            var changed = new NeteaseBoxHelperEvidence(NeteaseBoxHelperEvidenceKind.Valid, 30, Now.AddSeconds(-15).UtcTicks);
            Need(Assess(changed, proof).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：未知启动时间、未加载覆盖层和未来事件拒绝确认", () =>
        {
            var proof = Assess(Current).Session;
            Need(Assess(Current, target: Target with { StartTimeUtcTicks = 0 }).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(Current, box: Box with { StartTimeUtcTicks = 0 }).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(NeteaseBoxHelperEvidence.Missing, proof, target: Target with { HasOverlay = false }).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(Current with { EventTimeUtcTicks = Now.AddSeconds(10).UtcTicks }).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：缺失、损坏和无法读取的日志不伪报已确认", () =>
        {
            foreach (var evidence in new[] { NeteaseBoxHelperEvidence.Missing, NeteaseBoxHelperEvidence.Malformed, NeteaseBoxHelperEvidence.Unreadable })
                Need(Assess(evidence).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(NeteaseBoxHelperEvidence.Malformed, Assess(Current).Session).Verdict == NeteaseBoxBindingVerdict.Waiting);
            Need(Assess(NeteaseBoxHelperEvidence.Unreadable, Assess(Current).Session).Verdict == NeteaseBoxBindingVerdict.Waiting);
            return Task.CompletedTask;
        });
        await check("绑定：日志只读最新注入事件，不能越过冲突向前寻找匹配 PID", () =>
        {
            var log = Line(20, Now.AddMinutes(-1)) + "\n" + Line(30, Now.AddSeconds(-15));
            var latest = NeteaseBoxTargetBinding.ParseLatestHelperTarget(log);
            Need(latest.Kind == NeteaseBoxHelperEvidenceKind.Valid && latest.TargetProcessId == 30 && latest.EventTimeUtcTicks == Now.AddSeconds(-15).UtcTicks);
            var temp = NewDirectory();
            try
            {
                var path = Path.Combine(temp, "heart_helper.log"); File.WriteAllText(path, log);
                Need(NeteaseBoxTargetBinding.TryReadRecentHelperTarget(path, Now.AddMinutes(-5).LocalDateTime, expectedTargetProcessId: 20) == null);
            }
            finally { Directory.Delete(temp, true); }
            return Task.CompletedTask;
        });
        await check("绑定：最新注入记录损坏或尚未写完时不回退旧成功记录", () =>
        {
            foreach (var bad in new[] { "[bad date] start inject, target pid 30", "[2026-10-06 20:00:00] start inject, target pid bad", "[2026-10-06 20:00:00] start inject, target pid" })
                Need(NeteaseBoxTargetBinding.ParseLatestHelperTarget(Line(20, Now.AddMinutes(-1)) + "\n" + bad).Kind == NeteaseBoxHelperEvidenceKind.Malformed);
            return Task.CompletedTask;
        });
        await check("绑定：文件缺失、空文件和无法读取的路径都有区分", () =>
        {
            var temp = NewDirectory();
            try
            {
                var path = Path.Combine(temp, "heart_helper.log");
                Need(NeteaseBoxTargetBinding.ReadLatestHelperTarget(path).Kind == NeteaseBoxHelperEvidenceKind.Missing);
                File.WriteAllText(path, ""); Need(NeteaseBoxTargetBinding.ReadLatestHelperTarget(path).Kind == NeteaseBoxHelperEvidenceKind.Missing);
                Need(NeteaseBoxTargetBinding.ReadLatestHelperTarget(temp).Kind == NeteaseBoxHelperEvidenceKind.Unreadable);
            }
            finally { Directory.Delete(temp, true); }
            return Task.CompletedTask;
        });
        await check("绑定：未确认连接只短时等待并准确超时，不伪报成功", async () =>
        {
            var clock = Stopwatch.StartNew(); bool timedOut = false;
            try { await NeteaseBoxBindingEvidence.WaitForVerificationAsync(() => Assess(NeteaseBoxHelperEvidence.Missing), TimeSpan.FromMilliseconds(100), default); }
            catch (TimeoutException ex) { timedOut = ex.Message.Contains("未能确认") && ex.Message.Contains("没有注销账号"); }
            Need(timedOut && clock.Elapsed < TimeSpan.FromSeconds(2));
        });
        await check("绑定：短时等待可取消，活动冲突立即拒绝", async () =>
        {
            using var cancel = new CancellationTokenSource(40); bool cancelled = false;
            try { await NeteaseBoxBindingEvidence.WaitForVerificationAsync(() => Assess(NeteaseBoxHelperEvidence.Missing), TimeSpan.FromSeconds(2), cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Need(cancelled);
            int probes = 0; bool rejected = false;
            try
            {
                await NeteaseBoxBindingEvidence.WaitForVerificationAsync(() =>
                {
                    probes++;
                    return Assess(Current with { TargetProcessId = 30 }, other: new(30, Now.AddMinutes(-4).UtcTicks, true));
                }, TimeSpan.FromSeconds(2), default);
            }
            catch (InvalidOperationException ex) { rejected = ex.Message.Contains("活动炉石"); }
            Need(rejected && probes == 1);
        });
        await check("绑定：等待中得到真实当前证据后才能完成", async () =>
        {
            int probes = 0;
            var result = await NeteaseBoxBindingEvidence.WaitForVerificationAsync(
                () => ++probes == 1 ? Assess(NeteaseBoxHelperEvidence.Missing) : Assess(Current), TimeSpan.FromSeconds(2), default);
            Need(probes == 2 && result.Verdict == NeteaseBoxBindingVerdict.Verified);
        });
    }

    private static NeteaseBoxBindingAssessment Assess(NeteaseBoxHelperEvidence helper, NeteaseBoxVerifiedSession? verified = null,
        NeteaseBoxProcessSession? box = null, NeteaseBoxHearthstoneProcess? target = null, NeteaseBoxHearthstoneProcess? other = null)
    {
        var clients = other == null ? new[] { target ?? Target } : new[] { target ?? Target, other };
        return NeteaseBoxBindingEvidence.Assess((target ?? Target).ProcessId, box ?? Box, clients, helper, verified!, Now);
    }
    private static string Line(int pid, DateTimeOffset timestamp) => $"[{timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] start inject, target pid {pid}.";
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LegendRush-binding-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path;
    }
    private static void Need(bool value) { if (!value) throw new InvalidOperationException("Binding fixture assertion failed."); }
}
