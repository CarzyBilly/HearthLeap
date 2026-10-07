using System.Text.Json;
using HsAuto.Core.Automation;
using HsAuto.Core.Models;

namespace HsAuto.Open;

public static class BridgeHandshake
{
    public static bool Matches(BridgeResponse response, int pid)
    {
        var data = response.Data;
        return response.Ok && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("plugin", out var plugin) && plugin.ValueKind == JsonValueKind.String && plugin.GetString() == "HsAuto.OpenBridge"
            && data.TryGetProperty("processId", out var process) && process.ValueKind == JsonValueKind.Number && process.TryGetInt32(out var value) && value == pid
            && data.TryGetProperty("protocol", out var protocol) && protocol.ValueKind == JsonValueKind.Number && protocol.TryGetInt32(out var version) && version == 1;
    }
}

public static class StateSafety
{
    public static bool IsReadable(ConstructedGameState state) => state != null &&
        !(state.Tags.TryGetValue("reason", out var reason) && !string.IsNullOrWhiteSpace(reason));
    public static void RequireReadable(ConstructedGameState state)
    {
        if (!IsReadable(state)) throw new IOException("桥接局面读取失败，已停止；不会把错误当作大厅继续操作。原因：" + (state?.Tags.GetValueOrDefault("reason") ?? "empty-state"));
    }
}

public static class ShutdownDrain
{
    // Installation is a transaction: never abandon it during a window close.
    // Normal workers are cancelled by the caller; waiting does not block the UI thread.
    public static async Task DrainAsync(IEnumerable<Task> operations, Task criticalInstallation, Func<Task> save,
        TimeSpan workerTimeout, Action<string> report = null)
    {
        if (criticalInstallation != null)
        {
            try { await criticalInstallation; }
            catch (Exception ex) { report?.Invoke("安装退出：" + ex.Message); }
        }
        try { await Task.WhenAll(operations).WaitAsync(workerTimeout); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { report?.Invoke("后台任务退出：" + ex.Message); }
        try { await save(); }
        catch (Exception ex) { report?.Invoke("退出保存失败：" + ex.Message); }
    }
}
