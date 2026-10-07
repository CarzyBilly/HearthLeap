// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
public sealed class MainMenuStartupCoordinator
{
    public const string RecoveryStatus = "主界面奖励收尾超时请求恢复";
    private readonly Func<string, object, CancellationToken, Task<BridgeResponse>> _send;
    private readonly TimeProvider _time;
    private DateTimeOffset? _stableSince;
    private DateTimeOffset? _quietSince;
    private DateTimeOffset? _lastProgress;
    private DateTimeOffset _nextAction;
    private bool _complete;
    private bool _apprentice;
    private bool _stableLogged;
    private int _claimed;
    private int _failures;
    private int _dismissed;
    private int _startupPopupId;
    private bool _returningInProgress;
    private string _returningStage = "";
    private bool _mainMenuStableHandled;
    public string Status { get; private set; } = "等待主界面动画稳定";
    public bool StartScreenVisible { get; private set; }
    public Func<CancellationToken, Task>? MainMenuStable { get; set; }

    public MainMenuStartupCoordinator(UnityBridgeClient client) : this((string command, object arguments, CancellationToken token) => client.SendAsync(command, arguments, 8000, token), TimeProvider.System)
    {
    }

    internal MainMenuStartupCoordinator(Func<string, object, CancellationToken, Task<BridgeResponse>> send, TimeProvider time)
    {
        _send = send;
        _time = time;
    }

    public async Task<bool> TryHandleAsync(bool claimRewards, Action<string> log, CancellationToken token)
    {
        if (_complete)
        {
            return false;
        }

        if (Status == "主界面奖励收尾超时请求恢复")
        {
            return true;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (now < _nextAction)
        {
            return true;
        }

        _nextAction = now.AddSeconds(1.5);
        try
        {
            BridgeResponse state = await _send("mainMenuState", new { }, token);
            if (!state.Ok)
            {
                return Failed(log, state.Error);
            }

            if (!state.Data.TryGetProperty("atMainMenu", out var _) && !Bool(state.Data, "activeGameplay"))
            {
                return Failed(log, "主界面状态字段缺失");
            }

            StartScreenVisible = Bool(state.Data, "startScreen");
            if (Bool(state.Data, "activeGameplay"))
            {
                ResetReadiness();
                _returningInProgress = false;
                _returningStage = "";
                _lastProgress = null;
                _nextAction = default;
                return false;
            }

            DateTimeOffset value2;
            DateTimeOffset? lastProgress;
            if (Bool(state.Data, "isReturningPlayer") && (Bool(state.Data, "returningStartupPending") || (_returningInProgress && !Bool(state.Data, "ready") && !StartScreenVisible)))
            {
                if (!_returningInProgress)
                {
                    _returningInProgress = true;
                    log("游戏已确认回归账号，先完成回归动画与弹窗，再准备主界面");
                    ResetReadiness();
                }

                string text = state.Data.GetProperty("returningStage").GetString() ?? "";
                if (text != _returningStage)
                {
                    _returningStage = text;
                    _lastProgress = now;
                    ResetReadiness();
                }

                _lastProgress.GetValueOrDefault();
                if (!_lastProgress.HasValue)
                {
                    _lastProgress = now;
                }

                value2 = now;
                lastProgress = _lastProgress;
                if (value2 - lastProgress >= TimeSpan.FromSeconds(120.0) || _dismissed >= 100)
                {
                    Status = "主界面奖励收尾超时请求恢复";
                    return true;
                }

                _stableSince.GetValueOrDefault();
                if (!_stableSince.HasValue)
                {
                    _stableSince = now;
                }

                Status = "回归账号：等待动画与弹窗稳定";
                value2 = now;
                lastProgress = _stableSince;
                if (value2 - lastProgress < TimeSpan.FromSeconds(8.0))
                {
                    return true;
                }

                BridgeResponse bridgeResponse = await _send("advanceReturningPlayerStartup", new { expectedStage = text }, token);
                if (!bridgeResponse.Ok)
                {
                    return Failed(log, bridgeResponse.Error);
                }

                if (Bool(bridgeResponse.Data, "deferred"))
                {
                    ResetReadiness();
                    return true;
                }

                if (Bool(bridgeResponse.Data, "dismissed"))
                {
                    _dismissed++;
                    _failures = 0;
                    string text2 = (bridgeResponse.Data.TryGetProperty("kind", out var value3) ? value3.GetString() : "启动提示");
                    log("回归账号：已推进" + text2 + "，等待后续动画或奖励");
                }

                _nextAction = _time.GetUtcNow().AddSeconds(3.0);
                return true;
            }

            if (_returningInProgress)
            {
                _returningInProgress = false;
                _returningStage = "";
                ResetReadiness();
                _lastProgress = now;
                if (Bool(state.Data, "ready"))
                {
                    log("回归引导已结束，已回到正常主界面，继续奖励收尾和脚本自动化");
                }
            }

            if (Bool(state.Data, "startupPending"))
            {
                StartScreenVisible = false;
                _lastProgress.GetValueOrDefault();
                if (!_lastProgress.HasValue)
                {
                    _lastProgress = now;
                }

                value2 = now;
                lastProgress = _lastProgress;
                if (value2 - lastProgress >= TimeSpan.FromSeconds(120.0) || _dismissed >= 100)
                {
                    Status = "主界面奖励收尾超时请求恢复";
                    return true;
                }

                int popupId = Int(state.Data, "startupPopupId");
                if (popupId != _startupPopupId)
                {
                    ResetReadiness();
                    _startupPopupId = popupId;
                }

                _stableSince.GetValueOrDefault();
                if (!_stableSince.HasValue)
                {
                    _stableSince = now;
                }

                if (!_stableLogged)
                {
                    log((popupId == 0) ? "等待登录与开门流程稳定，8 秒后检查已知提示和奖励弹窗" : ("已识别启动后的弹窗（" + state.Data.GetProperty("startupPopupKind").GetString() + "），开始按钮已退出，等待 8 秒后收尾"));
                    _stableLogged = true;
                }

                Status = "等待启动奖励或任务弹窗稳定（8秒）";
                value2 = now;
                lastProgress = _stableSince;
                if (value2 - lastProgress < TimeSpan.FromSeconds(8.0))
                {
                    return true;
                }

                BridgeResponse bridgeResponse2 = await _send("dismissStartupPopup", new { expectedPopupId = popupId }, token);
                if (!bridgeResponse2.Ok)
                {
                    return Failed(log, bridgeResponse2.Error);
                }

                if (Bool(bridgeResponse2.Data, "deferred"))
                {
                    ResetReadiness();
                    return true;
                }

                if (Bool(bridgeResponse2.Data, "dismissed"))
                {
                    _dismissed++;
                    _lastProgress = now;
                    _failures = 0;
                    string text3 = ((bridgeResponse2.Data.TryGetProperty("kind", out var value4) && value4.ValueKind == JsonValueKind.String) ? value4.GetString() : state.Data.GetProperty("startupPopupKind").GetString());
                    log("已关闭启动弹窗（" + text3 + "），等待主界面继续加载");
                    ResetReadiness();
                    _nextAction = _time.GetUtcNow().AddSeconds(2.0);
                }
                else if (popupId == 0)
                {
                    _nextAction = _time.GetUtcNow().AddSeconds(3.0);
                }

                return true;
            }

            if (_startupPopupId != 0)
            {
                ResetReadiness();
                _startupPopupId = 0;
            }

            if (Bool(state.Data, "startScreen"))
            {
                ResetReadiness();
                _lastProgress = null;
                _nextAction = default;
                return false;
            }

            if (!Bool(state.Data, "atMainMenu"))
            {
                ResetReadiness();
                _lastProgress = null;
                _nextAction = default;
                return false;
            }

            _lastProgress.GetValueOrDefault();
            if (!_lastProgress.HasValue)
            {
                _lastProgress = now;
            }

            value2 = now;
            lastProgress = _lastProgress;
            if (value2 - lastProgress >= TimeSpan.FromSeconds(120.0) || _claimed >= 200 || _dismissed >= 100)
            {
                Status = "主界面奖励收尾超时请求恢复";
                return true;
            }

            if (!Bool(state.Data, "ready"))
            {
                ResetReadiness();
                Status = "等待主界面动画稳定";
                return true;
            }

            _stableSince.GetValueOrDefault();
            if (!_stableSince.HasValue)
            {
                _stableSince = now;
            }

            value2 = now;
            lastProgress = _stableSince;
            if (value2 - lastProgress < TimeSpan.FromSeconds(8.0))
            {
                if (!_stableLogged)
                {
                    log("主界面已就绪，等待 8 秒让开门动画和登录奖励弹窗稳定");
                    _stableLogged = true;
                }

                Status = "等待主界面动画稳定（8秒）";
                return true;
            }

            if (!_mainMenuStableHandled)
            {
                if (MainMenuStable != null)
                {
                    await MainMenuStable(token);
                }

                _mainMenuStableHandled = true;
            }

            string[] array = new string[3]
            {
                "dismissInGameMessageModal",
                "dismissNavigationPopup",
                "dismissRewardPopup"
            };
            foreach (string command in array)
            {
                BridgeResponse bridgeResponse3 = await _send(command, new { requireMainMenuReady = true, includeEndOfGameXp = false }, token);
                if (!bridgeResponse3.Ok)
                {
                    return Failed(log, bridgeResponse3.Error);
                }

                if (Bool(bridgeResponse3.Data, "deferred"))
                {
                    ResetReadiness();
                    return true;
                }

                if (Bool(bridgeResponse3.Data, "dismissed") || Bool(bridgeResponse3.Data, "visible"))
                {
                    _quietSince = null;
                    Status = "等待主界面奖励弹窗动画";
                    _nextAction = _time.GetUtcNow().AddSeconds(2.0);
                    if (Bool(bridgeResponse3.Data, "dismissed"))
                    {
                        _dismissed++;
                        _lastProgress = now;
                        string text4 = (bridgeResponse3.Data.TryGetProperty("kind", out var value5) ? value5.GetString() : command);
                        log("已关闭主界面弹窗（" + text4 + "），等待 2 秒后复核后续奖励");
                    }

                    return true;
                }
            }

            if (Bool(state.Data, "rewardPending"))
            {
                _quietSince = null;
                Status = "等待通行证奖励队列显示完成";
                return true;
            }

            _quietSince.GetValueOrDefault();
            if (!_quietSince.HasValue)
            {
                value2 = _time.GetUtcNow();
                _quietSince = value2;
            }

            value2 = _time.GetUtcNow();
            lastProgress = _quietSince;
            if (value2 - lastProgress < TimeSpan.FromSeconds(3.0))
            {
                Status = "复核主界面无后续奖励弹窗（3秒）";
                return true;
            }

            if (!claimRewards)
            {
                _complete = true;
                log($"主界面准备完成：关闭弹窗 {_dismissed} 次，已确认登录奖励收尾；通行证按匹配前设置处理");
                return false;
            }

            BridgeResponse bridgeResponse4 = await _send("claimRewardTrackRewards", new { maxClaims = 1, claimChooseOne = _apprentice, includeApprenticeTracks = _apprentice, onlyTrackType = (_apprentice ? 8 : 0), maxRewards = 1, requireMainMenuReady = true }, token);
            if (!bridgeResponse4.Ok)
            {
                return Failed(log, bridgeResponse4.Error);
            }

            if (Bool(bridgeResponse4.Data, "deferred") || !Bool(bridgeResponse4.Data, "rewardTracksReady"))
            {
                _quietSince = null;
                Status = "等待通行证数据和奖励回包";
                return true;
            }

            if (Int(bridgeResponse4.Data, "failedCount") > 0)
            {
                return Failed(log, "内部领取暂未成功");
            }

            _failures = 0;
            int num = Int(bridgeResponse4.Data, "claimedCount");
            if (num > 0)
            {
                _claimed += num;
                _lastProgress = now;
                _quietSince = null;
                _nextAction = _time.GetUtcNow().AddSeconds(3.0);
                Status = "等待通行证领取回包及奖励弹窗（3秒）";
                log($"通行证逐项领取：已提交第 {_claimed} 项，等待 3 秒及弹窗收尾后再领下一项");
                return true;
            }

            if (!_apprentice)
            {
                if (Int(bridgeResponse4.Data, "skippedChooseOneCount") > 0)
                {
                    log("普通通行证多选一奖励保留手动选择");
                }

                _apprentice = true;
                return true;
            }

            _complete = true;
            log($"主界面准备完成：通行证领取 {_claimed} 项，关闭弹窗 {_dismissed} 次，已确认奖励队列和弹层收尾");
            return false;
        }
        catch (OperationCanceledException)when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex2)
        {
            return Failed(log, ex2.Message);
        }
    }

    private bool Failed(Action<string> log, string? error)
    {
        ResetReadiness();
        _nextAction = _time.GetUtcNow().AddSeconds(3.0);
        Status = ((++_failures >= 3) ? "主界面奖励收尾超时请求恢复" : "等待主界面奖励状态重试");
        log("主界面奖励检查暂未完成：" + error);
        return true;
    }

    private void ResetReadiness()
    {
        _stableSince = (_quietSince = null);
        _stableLogged = false;
    }

    private static bool Bool(JsonElement data, string name)
    {
        if (data.TryGetProperty(name, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static int Int(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
        {
            return 0;
        }

        return value2;
    }
}