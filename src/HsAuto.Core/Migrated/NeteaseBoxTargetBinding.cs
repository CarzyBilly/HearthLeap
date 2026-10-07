// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
public static class NeteaseBoxTargetBinding
{
    private static readonly TimeSpan TargetOverlayTimeout = TimeSpan.FromSeconds(55.0);
    private static readonly string[] OverlayModuleNames = new string[2]
    {
        "heart_overlay_x64.dll",
        "heart_overlay.dll"
    };
    public static NeteaseBoxTargetBindingState Evaluate(int targetProcessId, IReadOnlyCollection<NeteaseBoxHearthstoneProcess> clients)
    {
        NeteaseBoxHearthstoneProcess neteaseBoxHearthstoneProcess = clients.FirstOrDefault((NeteaseBoxHearthstoneProcess client) => client.ProcessId == targetProcessId);
        if ((object)neteaseBoxHearthstoneProcess == null)
        {
            return NeteaseBoxTargetBindingState.TargetMissing;
        }

        if (neteaseBoxHearthstoneProcess.HasOverlay)
        {
            return NeteaseBoxTargetBindingState.AlreadyBound;
        }

        if (clients.Count == 1)
        {
            return NeteaseBoxTargetBindingState.CanBindAsNewestClient;
        }

        long num = clients.Max((NeteaseBoxHearthstoneProcess client) => client.StartTimeUtcTicks);
        if (neteaseBoxHearthstoneProcess.StartTimeUtcTicks <= 0 || num <= 0 || neteaseBoxHearthstoneProcess.StartTimeUtcTicks != num)
        {
            return NeteaseBoxTargetBindingState.TargetIsNotNewestClient;
        }

        return NeteaseBoxTargetBindingState.CanBindAsNewestClient;
    }

    public static NeteaseBoxTargetBindingState Inspect(int targetProcessId)
    {
        IReadOnlyList<NeteaseBoxHearthstoneProcess> clients = SnapshotLiveClients();
        return Evaluate(targetProcessId, clients);
    }

    public static bool CanRestartBoxWithoutClientRestart(NeteaseBoxTargetBindingState state)
    {
        if ((uint)(state - 1) <= 1u)
        {
            return true;
        }

        return false;
    }

    public static bool IsNewestLiveClient(int targetProcessId)
    {
        IReadOnlyList<NeteaseBoxHearthstoneProcess> readOnlyList = SnapshotLiveClients();
        NeteaseBoxHearthstoneProcess neteaseBoxHearthstoneProcess = readOnlyList.FirstOrDefault((NeteaseBoxHearthstoneProcess client) => client.ProcessId == targetProcessId);
        if ((object)neteaseBoxHearthstoneProcess != null && (readOnlyList.Count == 1 || (neteaseBoxHearthstoneProcess.StartTimeUtcTicks > 0 && readOnlyList.Max((NeteaseBoxHearthstoneProcess client) => client.StartTimeUtcTicks) > 0)))
        {
            return neteaseBoxHearthstoneProcess.StartTimeUtcTicks == readOnlyList.Max((NeteaseBoxHearthstoneProcess client) => client.StartTimeUtcTicks);
        }

        return false;
    }

    public static void ValidateTargetClient(int targetProcessId, string strategyLabel)
    {
        switch (Inspect(targetProcessId))
        {
            case NeteaseBoxTargetBindingState.AlreadyBound:
            case NeteaseBoxTargetBindingState.CanBindAsNewestClient:
                break;
            case NeteaseBoxTargetBindingState.TargetIsNotNewestClient:
                throw new InvalidOperationException($"{strategyLabel}尚未绑定目标炉石 PID={targetProcessId}，且该客户端不是最近启动的炉石。" + "为避免盒子误绑其他账号，已停止初始化；请只重启需要使用盒子策略的目标账号客户端后重试，其他账号无需关闭。");
            default:
                throw new InvalidOperationException($"{strategyLabel}找不到当前账号的目标炉石 PID={targetProcessId}。");
        }
    }

    public static bool IsTargetOverlayLoaded(int targetProcessId)
    {
        try
        {
            using Process process = Process.GetProcessById(targetProcessId);
            return IsHearthstone(process) && HasOverlay(process);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int?> WaitForRecentHelperTargetAsync(string boxExecutablePath, DateTime boxStartedAtLocal, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(boxExecutablePath))
        {
            return null;
        }

        string directoryName = Path.GetDirectoryName(boxExecutablePath);
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return null;
        }

        string helperLogPath = Path.Combine(directoryName, "heart_helper.log");
        if (!File.Exists(helperLogPath))
        {
            return null;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(15.0);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? result = TryReadRecentHelperTarget(helperLogPath, boxStartedAtLocal.AddSeconds(-5.0));
            if (result.HasValue)
            {
                return result;
            }

            await Task.Delay(500, cancellationToken);
        }

        return null;
    }

    public static async Task WaitForTargetOverlayAsync(int targetProcessId, string strategyLabel, CancellationToken cancellationToken)
    {
        await WaitForTargetOverlayAsync(targetProcessId, strategyLabel, TargetOverlayTimeout, cancellationToken);
    }

    public static async Task WaitForTargetOverlayAsync(int targetProcessId, string strategyLabel, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException("timeout");
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsTargetOverlayLoaded(targetProcessId))
            {
                return;
            }

            await Task.Delay(1000, cancellationToken);
        }

        throw new TimeoutException($"{strategyLabel}在 {timeout.TotalSeconds:0} 秒内未绑定当前账号炉石 PID={targetProcessId}。" + "为避免读取其他账号的盒子推荐，已停止该账号的盒子策略。");
    }

    internal static IReadOnlyList<NeteaseBoxHearthstoneProcess> SnapshotLiveClients()
    {
        Process[] processesByName = Process.GetProcessesByName("Hearthstone");
        try
        {
            return (
                from process in processesByName
                where !SafeHasExited(process)select new NeteaseBoxHearthstoneProcess(process.Id, SafeStartTimeUtcTicks(process), HasOverlay(process))).ToArray();
        }
        finally
        {
            Process[] array = processesByName;
            for (int num = 0; num < array.Length; num++)
            {
                array[num].Dispose();
            }
        }
    }

    private static bool HasOverlay(Process process)
    {
        try
        {
            return ((IEnumerable)process.Modules).Cast<ProcessModule>().Any((ProcessModule module) => OverlayModuleNames.Contains(module.ModuleName, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    internal static int? TryReadRecentHelperTarget(string helperLogPath, DateTime notBeforeLocal, int? expectedTargetProcessId = null)
    {
        var evidence = ReadLatestHelperTarget(helperLogPath);
        if (evidence.Kind != NeteaseBoxHelperEvidenceKind.Valid || evidence.EventTimeUtcTicks < notBeforeLocal.ToUniversalTime().Ticks)
            return null;
        // Filter only the latest observation. Never search backward for a matching PID past a conflict.
        return !expectedTargetProcessId.HasValue || expectedTargetProcessId.Value == evidence.TargetProcessId
            ? evidence.TargetProcessId : null;
    }

    internal static NeteaseBoxHelperEvidence ReadLatestHelperTarget(string helperLogPath)
    {
        if (string.IsNullOrWhiteSpace(helperLogPath)) return NeteaseBoxHelperEvidence.Missing;
        try
        {
            using FileStream fileStream = new FileStream(helperLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int num = (int)Math.Min(fileStream.Length, 262144L);
            if (num <= 0)
            {
                return NeteaseBoxHelperEvidence.Missing;
            }

            fileStream.Seek(-num, SeekOrigin.End);
            byte[] array = new byte[num];
            fileStream.ReadExactly(array);
            return ParseLatestHelperTarget(Encoding.UTF8.GetString(array));
        }
        catch (FileNotFoundException)
        {
            return NeteaseBoxHelperEvidence.Missing;
        }
        catch (DirectoryNotFoundException)
        {
            return NeteaseBoxHelperEvidence.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return NeteaseBoxHelperEvidence.Unreadable;
        }
    }

    internal static NeteaseBoxHelperEvidence ParseLatestHelperTarget(string text)
    {
        string[] lines = (text ?? "").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i].TrimStart('\uFEFF');
            const string marker = "start inject, target pid ";
            int markerOffset = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (line.IndexOf("start inject", StringComparison.OrdinalIgnoreCase) < 0) continue;
            int bracketOffset = line.IndexOf(']');
            if (markerOffset < 0 || line.Length < 3 || line[0] != '[' || bracketOffset <= 1 ||
                !DateTime.TryParse(line.Substring(1, bracketOffset - 1), CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var timestamp) ||
                !int.TryParse(line[(markerOffset + marker.Length)..].Trim().TrimEnd('.'), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var pid) || pid <= 0)
                return NeteaseBoxHelperEvidence.Malformed;
            return new(NeteaseBoxHelperEvidenceKind.Valid, pid, timestamp.ToUniversalTime().Ticks);
        }
        return NeteaseBoxHelperEvidence.Missing;
    }

    private static bool IsHearthstone(Process process)
    {
        try
        {
            return !process.HasExited && string.Equals(process.ProcessName, "Hearthstone", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static long SafeStartTimeUtcTicks(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return 0L;
        }
    }
}
