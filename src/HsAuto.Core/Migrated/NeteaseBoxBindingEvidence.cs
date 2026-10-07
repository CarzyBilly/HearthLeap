// Local connection evidence only. This policy never reads account credentials or changes a process.
namespace HsAuto.Core.Automation;

internal enum NeteaseBoxHelperEvidenceKind { Missing, Valid, Malformed, Unreadable }
internal enum NeteaseBoxBindingVerdict { Waiting, Verified, Conflict }

// Lets the controller distinguish the expected 8-second evidence timeout from
// a permanent account/process conflict. It is still a normal timeout to UI
// callers, but the recovery supervisor may safely retry it once the box is
// restarted.
public sealed class NeteaseBoxBindingTimeoutException(string message) : TimeoutException(message);

internal sealed record NeteaseBoxHelperEvidence(NeteaseBoxHelperEvidenceKind Kind, int TargetProcessId = 0, long EventTimeUtcTicks = 0)
{
    internal static readonly NeteaseBoxHelperEvidence Missing = new(NeteaseBoxHelperEvidenceKind.Missing);
    internal static readonly NeteaseBoxHelperEvidence Malformed = new(NeteaseBoxHelperEvidenceKind.Malformed);
    internal static readonly NeteaseBoxHelperEvidence Unreadable = new(NeteaseBoxHelperEvidenceKind.Unreadable);
}

internal sealed record NeteaseBoxProcessSession(int ProcessId, long StartTimeUtcTicks, string ExecutablePath);
internal sealed record NeteaseBoxVerifiedSession(int BoxProcessId, long BoxStartTimeUtcTicks, string BoxExecutablePath, int TargetProcessId, long TargetStartTimeUtcTicks, long HelperEventTimeUtcTicks)
{
    internal bool Matches(NeteaseBoxProcessSession box, NeteaseBoxHearthstoneProcess target) =>
        BoxProcessId == box.ProcessId && BoxStartTimeUtcTicks == box.StartTimeUtcTicks &&
        TargetProcessId == target.ProcessId && TargetStartTimeUtcTicks == target.StartTimeUtcTicks &&
        string.Equals(BoxExecutablePath, box.ExecutablePath, StringComparison.OrdinalIgnoreCase);
}

internal sealed record NeteaseBoxBindingAssessment(NeteaseBoxBindingVerdict Verdict, string Reason, NeteaseBoxVerifiedSession Session = null, bool Reused = false);

internal static class NeteaseBoxBindingEvidence
{
    // Timestamp comparisons are deliberately strict: an injection attempt before the current process
    // started must not authenticate a reused PID. A loaded overlay is also required for completion.
    internal static NeteaseBoxBindingAssessment Assess(
        int targetProcessId, NeteaseBoxProcessSession box, IReadOnlyCollection<NeteaseBoxHearthstoneProcess> clients,
        NeteaseBoxHelperEvidence helper, NeteaseBoxVerifiedSession verified, DateTimeOffset now)
    {
        var target = clients.SingleOrDefault(client => client.ProcessId == targetProcessId);
        if (target == null) return Wait("当前炉石进程已退出或暂时无法读取。");
        if (box == null || box.ProcessId <= 0 || box.StartTimeUtcTicks <= 0 || string.IsNullOrWhiteSpace(box.ExecutablePath))
            return Wait("无法读取盒子进程身份；请保持盒子与本软件权限一致。");
        if (target.StartTimeUtcTicks <= 0) return Wait("无法读取炉石进程启动时间，不能确认当前连接。");
        helper ??= NeteaseBoxHelperEvidence.Missing;
        if (helper.Kind == NeteaseBoxHelperEvidenceKind.Malformed)
            return Wait("助手最新连接记录损坏或尚未写完；不会回退使用更早的绑定记录。");
        if (helper.Kind == NeteaseBoxHelperEvidenceKind.Unreadable)
            return Wait("无法读取助手连接日志；请保持软件权限一致。");

        string pending = "助手日志尚未提供当前炉石实例的连接记录。";
        if (helper.Kind == NeteaseBoxHelperEvidenceKind.Valid)
        {
            if (helper.TargetProcessId <= 0 || helper.EventTimeUtcTicks <= 0 || helper.EventTimeUtcTicks > now.AddSeconds(2).UtcTicks)
                return Wait("助手最新连接时间无效，不能确认当前连接。");
            var loggedClient = clients.SingleOrDefault(client => client.ProcessId == helper.TargetProcessId);
            bool belongsToBox = helper.EventTimeUtcTicks >= box.StartTimeUtcTicks;
            bool belongsToLoggedClient = loggedClient != null && loggedClient.StartTimeUtcTicks > 0 &&
                helper.EventTimeUtcTicks >= loggedClient.StartTimeUtcTicks;
            if (belongsToBox && loggedClient != null && loggedClient.ProcessId != targetProcessId &&
                (loggedClient.StartTimeUtcTicks <= 0 || belongsToLoggedClient))
                return new(NeteaseBoxBindingVerdict.Conflict,
                    $"助手最新记录关联其他活动炉石 PID={loggedClient.ProcessId}；为避免跨账号读取推荐，已拒绝当前连接。");
            if (belongsToBox && belongsToLoggedClient && helper.TargetProcessId == targetProcessId)
            {
                if (!target.HasOverlay) return Wait("助手已发起当前炉石连接，但目标覆盖层尚未加载完成。");
                return Verified(box, target, helper.EventTimeUtcTicks, reused: verified?.Matches(box, target) == true);
            }
            pending = loggedClient == null
                ? "助手最后记录属于已退出炉石，是历史记录而非当前活动账号冲突。"
                : "助手最后记录早于当前盒子或炉石实例启动；不会把旧记录用于复用 PID。";
        }

        // Reuse an already proven, unchanged session only when there is no other live game that could
        // own the box. Missing/rotated historical logs cannot turn an unverified session into success.
        bool noNewerBindingEvent = helper.Kind == NeteaseBoxHelperEvidenceKind.Missing ||
            helper.EventTimeUtcTicks <= (verified?.HelperEventTimeUtcTicks ?? 0);
        if (verified?.Matches(box, target) == true && target.HasOverlay && clients.Count == 1 && noNewerBindingEvent)
            return Verified(box, target, verified.HelperEventTimeUtcTicks, reused: true);
        if (!target.HasOverlay) pending += "当前炉石覆盖层尚未加载。";
        if (clients.Count > 1) pending += "存在多个炉石实例，需要助手最新记录明确目标。";
        return Wait(pending);
    }

    private static NeteaseBoxBindingAssessment Wait(string reason) => new(NeteaseBoxBindingVerdict.Waiting, reason);
    private static NeteaseBoxBindingAssessment Verified(NeteaseBoxProcessSession box, NeteaseBoxHearthstoneProcess target, long helperEventTimeUtcTicks, bool reused) =>
        new(NeteaseBoxBindingVerdict.Verified, reused ? "复用已核实且未变化的本机连接。" : "已核实当前炉石实例的助手记录与覆盖层。",
            new(box.ProcessId, box.StartTimeUtcTicks, box.ExecutablePath, target.ProcessId, target.StartTimeUtcTicks, helperEventTimeUtcTicks), reused);

    internal static async Task<NeteaseBoxBindingAssessment> WaitForVerificationAsync(
        Func<NeteaseBoxBindingAssessment> probe, TimeSpan timeout, CancellationToken cancellationToken, Action<string> waiting = null)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool reported = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assessment = probe();
            if (assessment.Verdict == NeteaseBoxBindingVerdict.Verified) return assessment;
            if (assessment.Verdict == NeteaseBoxBindingVerdict.Conflict) throw new InvalidOperationException(assessment.Reason);
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
                throw new NeteaseBoxBindingTimeoutException($"在 {timeout.TotalSeconds:0.#} 秒内未能确认盒子与当前炉石实例的连接。{assessment.Reason}" +
                    "请在盒子中连接当前炉石后重试；本软件没有注销账号、删除绑定或重启进程。");
            if (!reported) { waiting?.Invoke(assessment.Reason); reported = true; }
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
