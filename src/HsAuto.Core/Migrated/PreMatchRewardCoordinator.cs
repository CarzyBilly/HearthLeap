// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
public sealed class PreMatchRewardCoordinator
{
    public const string RecoveryStatus = "通行证奖励收尾超时请求恢复";
    private readonly Func<string, object, CancellationToken, Task<BridgeResponse>> _send;
    private readonly TimeProvider _time;
    public PreMatchRewardCoordinator(UnityBridgeClient client) : this((string command, object args, CancellationToken ct) => client.SendAsync(command, args, 15000, ct), TimeProvider.System)
    {
    }

    internal PreMatchRewardCoordinator(Func<string, object, CancellationToken, Task<BridgeResponse>> send, TimeProvider time)
    {
        _send = send;
        _time = time;
    }

    public async Task<AchievementClaimResult> ClaimBeforeMatchAsync(Action<string> log, CancellationToken ct)
    {
        DateTimeOffset started = _time.GetUtcNow();
        DateTimeOffset lastProgress = started;
        DateTimeOffset quietSince = started;
        int claimed = 0;
        int phase = 0;
        int failures = 0;
        bool submitted = false;
        try
        {
            while (_time.GetUtcNow() - started < TimeSpan.FromMinutes(10.0) && _time.GetUtcNow() - lastProgress < TimeSpan.FromSeconds(120.0) && claimed < 200)
            {
                BridgeResponse bridgeResponse = await _send("drainPreMatchRewards", new { }, ct);
                if (!bridgeResponse.Ok)
                {
                    int num = failures + 1;
                    failures = num;
                    if (num >= 3)
                    {
                        return AchievementClaimResult.Recover;
                    }

                    await Pause(ct);
                    continue;
                }

                if (Bool(bridgeResponse.Data, "unsafeToStart"))
                {
                    return AchievementClaimResult.Deferred;
                }

                if (Bool(bridgeResponse.Data, "popupDismissed"))
                {
                    lastProgress = _time.GetUtcNow();
                }

                if (Bool(bridgeResponse.Data, "cleanupPending"))
                {
                    quietSince = _time.GetUtcNow();
                    await Pause(ct);
                    continue;
                }

                if (submitted && _time.GetUtcNow() - quietSince < TimeSpan.FromSeconds(3.0))
                {
                    await Pause(ct);
                    continue;
                }

                if (phase == 2)
                {
                    log($"匹配前通行证检查完成：领取 {claimed} 项，奖励弹窗已收尾");
                    return AchievementClaimResult.Ready;
                }

                BridgeResponse bridgeResponse2 = await _send("claimRewardTrackRewards", new { beforeMatch = true, maxClaims = 1, maxRewards = 1, claimChooseOne = (phase == 1), includeApprenticeTracks = (phase == 1), onlyTrackType = ((phase == 1) ? 8 : 0) }, ct);
                if (!bridgeResponse2.Ok || Int(bridgeResponse2.Data, "failedCount") > 0)
                {
                    int num = failures + 1;
                    failures = num;
                    if (num >= 3)
                    {
                        return AchievementClaimResult.Recover;
                    }

                    await Pause(ct);
                    continue;
                }

                if (Bool(bridgeResponse2.Data, "unsafeToStart"))
                {
                    return AchievementClaimResult.Deferred;
                }

                if (Bool(bridgeResponse2.Data, "deferred") || !Bool(bridgeResponse2.Data, "rewardTracksReady"))
                {
                    await Pause(ct);
                    continue;
                }

                failures = 0;
                int num2 = Int(bridgeResponse2.Data, "claimedCount");
                if (num2 > 0)
                {
                    claimed += num2;
                    submitted = true;
                    DateTimeOffset utcNow;
                    lastProgress = (utcNow = _time.GetUtcNow());
                    quietSince = utcNow;
                    log($"匹配前通行证逐项领取：已提交 {claimed} 项，等待回包和奖励收尾");
                    await Pause(ct);
                }
                else
                {
                    phase++;
                }
            }
        }
        catch (OperationCanceledException)when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex2)
        {
            log("匹配前通行证检查未完成：" + ex2.Message);
        }

        return AchievementClaimResult.Recover;
    }

    private Task Pause(CancellationToken ct)
    {
        return Task.Delay(TimeSpan.FromMilliseconds(500.0), _time, ct);
    }

    private static bool Bool(JsonElement d, string n)
    {
        if (d.TryGetProperty(n, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static int Int(JsonElement d, string n)
    {
        if (!d.TryGetProperty(n, out var value) || !value.TryGetInt32(out var value2))
        {
            return 0;
        }

        return value2;
    }
}