using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace HsAuto.Core.Automation;

// The open-source protocol is deliberately separate from the licensed legacy bridge.
public sealed class UnityBridgeClient
{
    public string PipeName { get; }
    public bool CaptureStateReadDetails { get; set; }
    public event Action<UnityBridgeCallTiming> CallCompleted;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public UnityBridgeClient(string pipeName = "HsAuto.OpenBridge") => PipeName = pipeName;
    public async Task<BridgeResponse> SendAsync(string command, object arguments = null, int timeoutMs = 3000, CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp(); var ok = false; string error = null; int size = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Clamp(timeoutMs, 100, 15000));
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, Encoding.UTF8, true, 4096, true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            string request = JsonSerializer.Serialize(new { command, arguments = arguments ?? new { }, protocol = 1, requestId = Guid.NewGuid().ToString("N") }, Json);
            await writer.WriteLineAsync(request.AsMemory(), timeout.Token);
            var line = await ReadBoundedAsync(reader, timeout.Token);
            size = line.Length;
            var response = JsonSerializer.Deserialize<BridgeResponse>(line, Json) ?? throw new IOException("Bridge 响应为空。");
            ok = response.Ok; error = response.Error;
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            error = $"Bridge '{command}' 超时（{timeoutMs} ms）；不会自动重发动作。";
            return new BridgeResponse(false, error, JsonSerializer.SerializeToElement(new { }));
        }
        finally { CallCompleted?.Invoke(new(PipeName, command, ok, Stopwatch.GetElapsedTime(start), size, error)); }
    }
    internal static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder(); var buffer = new char[1];
        while (result.Length <= 2 * 1024 * 1024)
        {
            if (await reader.ReadAsync(buffer.AsMemory(), ct) == 0) throw new IOException("Bridge 断开，未收到完整响应。");
            if (buffer[0] == '\n') return result.ToString().TrimEnd('\r');
            result.Append(buffer[0]);
        }
        throw new IOException("Bridge 响应超过 2 MiB。");
    }
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default) => (await SendAsync("ping", timeoutMs: 1200, cancellationToken: cancellationToken)).Ok;
}
