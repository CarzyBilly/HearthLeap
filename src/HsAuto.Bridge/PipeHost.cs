using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;

namespace HsAuto.UnityBridge {
[BepInPlugin("local.hsauto.openbridge", "HS Auto Open Bridge", "0.1.3")]
public sealed partial class OpenBridgePlugin
{
    readonly ConcurrentQueue<MainThreadCall> calls = new ConcurrentQueue<MainThreadCall>();
    volatile bool running;
    NamedPipeServerStream activePipe;
    Thread worker;
    string pipeName;
    sealed class BridgeRequest {
        public string Command { get; set; }
        public string ArgumentsJson { get; set; } = "{}";
        public static BridgeRequest Parse(string line) {
            if (GetInt(line, "protocol", 0) != 1) throw new InvalidDataException("OpenBridge protocol must be 1.");
            var command = GetString(line, "command");
            if (string.IsNullOrWhiteSpace(command) || command.Length > 64) throw new InvalidDataException("Invalid command.");
            return new BridgeRequest {Command = command, ArgumentsJson = ExtractObject(line, "arguments") ?? "{}"};
        }
    }
    sealed class MainThreadCall {
        public BridgeRequest Request;
        public DateTime Deadline = DateTime.UtcNow.AddSeconds(8);
        public TaskCompletionSource<BridgeResponse> Result = new TaskCompletionSource<BridgeResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    void Awake() {
        pipeName = "HsAuto.OpenBridge." + Process.GetCurrentProcess().Id;
        running = true;
        worker = new Thread(ServerLoop) { IsBackground = true, Name = "HS Auto OpenBridge" };
        worker.Start();
        Logger.LogInfo("Open-source bridge ready. Pipe=" + pipeName + "; no licence, login hooks, anti-cheat patches or hardware spoofing.");
    }
    void OnDestroy() { running = false; try { activePipe?.Dispose(); } catch {} }
    void Update() {
        // Bound per-frame work. An expired request is never executed later.
        for (int i = 0; i < 2 && calls.TryDequeue(out var call); ++i) {
            if (DateTime.UtcNow > call.Deadline || call.Result.Task.IsCompleted) { call.Result.TrySetResult(BridgeResponse.Failure("Expired before main-thread execution.")); continue; }
            try { call.Result.TrySetResult(HandleOpenRequest(call.Request)); }
            catch (Exception ex) { call.Result.TrySetResult(BridgeResponse.Failure(ex.GetType().Name + ": " + ex.Message)); }
        }
    }
    void ServerLoop() {
        while (running) {
            try {
                using (var pipe = SecurePipe.Create(pipeName)) {
                    activePipe = pipe;
                    pipe.WaitForConnection();
                    using (var reader = new StreamReader(pipe, Encoding.UTF8, true, 4096, true))
                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true }) {
                        var read = ReadRequest(reader);
                        if (!read.Wait(3000)) continue;
                        var request = BridgeRequest.Parse(read.GetAwaiter().GetResult());
                        BridgeResponse response;
                        if (request.Command == "ping") response = BridgeResponse.Success(new {plugin="HsAuto.OpenBridge",version="0.1.3",protocol=1,processId=Process.GetCurrentProcess().Id,pipeName});
                        else {
                            var call = new MainThreadCall { Request = request }; calls.Enqueue(call);
                            if (!call.Result.Task.Wait(9000)) { call.Result.TrySetResult(BridgeResponse.Failure("Main thread timeout; request abandoned.")); }
                            response = call.Result.Task.GetAwaiter().GetResult();
                        }
                        var write = writer.WriteLineAsync(response.ToJson()); write.Wait(3000);
                    }
                }
            } catch (Exception ex) { if (running) { Logger.LogWarning("Pipe: " + ex.GetType().Name + ": " + ex.Message); Thread.Sleep(200); } }
            finally { activePipe = null; }
        }
    }
    static async Task<string> ReadRequest(StreamReader reader) {
        var text = new StringBuilder(); var buf = new char[1];
        while (text.Length < 65536) {
            if (await reader.ReadAsync(buf,0,1) == 0) throw new IOException("Request disconnected.");
            if (buf[0] == '\n') return text.ToString();
            text.Append(buf[0]);
        }
        throw new InvalidDataException("Request too large.");
    }
}
}
