// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
public sealed class HearthstoneMatchmakingMonitor
{
    public static readonly TimeSpan MatchmakingTimeout = TimeSpan.FromMinutes(5.0);
    private static readonly TimeSpan NormalMatchTransitionGrace = TimeSpan.FromSeconds(45.0);
    private readonly UnityBridgeClient _client;
    private readonly TimeSpan _timeout;
    private readonly MatchmakingObservationClock _clock = new MatchmakingObservationClock();
    private DateTimeOffset _lastStateFailureLogAt = DateTimeOffset.MinValue;
    private int _lastLoggedPopupInstanceId;
    private int _lastLoggedMinute = -1;
    private DateTimeOffset _normalMatchTransitionUntil = DateTimeOffset.MinValue;
    private long _matchingObservationSequence;
    private long _matchStartRequestSequence;
    public long MatchingObservationSequence => Interlocked.Read(in _matchingObservationSequence);
    public long MatchStartRequestSequence => Interlocked.Read(in _matchStartRequestSequence);

    public event Action<string>? Log;
    public HearthstoneMatchmakingMonitor(UnityBridgeClient client, TimeSpan? timeout = null)
    {
        _client = client;
        _timeout = timeout ?? MatchmakingTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException("timeout");
        }
    }

    public void MarkMatchStartRequested()
    {
        Interlocked.Increment(ref _matchStartRequestSequence);
        _clock.MarkMatchStartRequested(DateTimeOffset.UtcNow);
    }

    public void ResetForConfirmedGameOver()
    {
        _clock.Reset();
        _normalMatchTransitionUntil = DateTimeOffset.MinValue;
        _lastLoggedPopupInstanceId = 0;
        _lastLoggedMinute = -1;
    }

    public async Task<MatchmakingMonitorResult> CheckAndRecoverAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse;
        try
        {
            bridgeResponse = await _client.SendAsync("matchmakingState", new { }, 8000, cancellationToken);
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex2)
        {
            LogStateFailure(ex2.Message);
            return MatchmakingMonitorResult.Unavailable;
        }

        if (!bridgeResponse.Ok)
        {
            LogStateFailure(bridgeResponse.Error ?? "UnityBridge 未返回成功");
            return MatchmakingMonitorResult.Unavailable;
        }

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        if (!ReadBool(bridgeResponse.Data, "isMatching"))
        {
            bool isMatching = _clock.IsMatching;
            _clock.ObserveNotMatching(utcNow);
            if (isMatching)
            {
                _normalMatchTransitionUntil = utcNow.Add(NormalMatchTransitionGrace);
                LogMessage("匹配弹窗已正常消失，判定为已匹配到对手；等待进入对局，不会重新点击开始按钮。");
            }

            _lastLoggedPopupInstanceId = 0;
            _lastLoggedMinute = -1;
            return (_normalMatchTransitionUntil > utcNow || _clock.HasRecentPendingStart(utcNow)) ? MatchmakingMonitorResult.TransitioningToGame : MatchmakingMonitorResult.NotMatching;
        }

        _normalMatchTransitionUntil = DateTimeOffset.MinValue;
        Interlocked.Increment(ref _matchingObservationSequence);
        int num = ReadInt(bridgeResponse.Data, "popupInstanceId");
        int num2 = ReadInt(bridgeResponse.Data, "cancelButtonInstanceId");
        bool flag = ReadBool(bridgeResponse.Data, "canCancel") && IsValidUnityInstanceId(num2);
        TimeSpan bridgeElapsed = TimeSpan.FromSeconds(Math.Max(0.0, ReadDouble(bridgeResponse.Data, "elapsedSeconds")));
        TimeSpan timeSpan = _clock.ObserveMatching(num, bridgeElapsed, utcNow);
        int num3 = (int)Math.Floor(timeSpan.TotalMinutes);
        if (_lastLoggedPopupInstanceId != num)
        {
            _lastLoggedPopupInstanceId = num;
            _lastLoggedMinute = num3;
            LogMessage($"已确认当前处于匹配中：MatchingPopup3D 实例 {num}，" + (flag ? $"取消按钮实例 {num2}，" : "尚未解析到安全取消按钮，") + "累计 " + FormatElapsed(timeSpan) + "。");
        }
        else if (num3 != _lastLoggedMinute && num3 > 0)
        {
            _lastLoggedMinute = num3;
            LogMessage($"当前匹配累计 {FormatElapsed(timeSpan)}，超时阈值为 {FormatElapsed(_timeout)}。");
        }

        if (timeSpan < _timeout)
        {
            return new MatchmakingMonitorResult(IsMatching: true, RecoveryTriggered: false, timeSpan, num, num2, flag)
            {
                StateReadSucceeded = true
            };
        }

        LogMessage($"匹配累计 {FormatElapsed(timeSpan)} 已达到超时阈值 {FormatElapsed(_timeout)}，请求重启炉石客户端和脚本。");
        return new MatchmakingMonitorResult(IsMatching: true, RecoveryTriggered: true, timeSpan, num, num2, flag)
        {
            StateReadSucceeded = true,
            RestartRequested = true
        };
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        int num = Math.Max(0, (int)Math.Floor(elapsed.TotalSeconds));
        return $"{num / 60:00}:{num % 60:00}";
    }

    public static bool IsValidUnityInstanceId(int instanceId)
    {
        return instanceId != 0;
    }

    private void LogStateFailure(string error)
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        if (!(utcNow - _lastStateFailureLogAt < TimeSpan.FromMinutes(1.0)))
        {
            _lastStateFailureLogAt = utcNow;
            LogMessage("匹配状态精确检测暂不可用：" + error);
        }
    }

    private void LogMessage(string message)
    {
        Log?.Invoke(message);
    }

    private static bool ReadBool(JsonElement data, string property)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static int ReadInt(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value))
        {
            return 0;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
            {
                if (value.TryGetInt32(out var value2))
                {
                    return value2;
                }

                break;
            }

            case JsonValueKind.String:
            {
                if (int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }

                break;
            }
        }

        return 0;
    }

    private static double ReadDouble(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value))
        {
            return 0.0;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
            {
                if (value.TryGetDouble(out var value2))
                {
                    return value2;
                }

                break;
            }

            case JsonValueKind.String:
            {
                if (double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }

                break;
            }
        }

        return 0.0;
    }
}