// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
public sealed class AchievementClaimCoordinator(UnityBridgeClient client)
{
    public const string RecoveryStatus = "成就奖励收尾超时请求恢复";
    public async Task<AchievementClaimResult> ClaimBeforeMatchAsync(Action<string> log, CancellationToken cancellationToken)
    {
        Stopwatch timer = Stopwatch.StartNew();
        int claimed = 0;
        int dismissed = 0;
        int emptyChecks = 0;
        bool submittedAny = false;
        int failures = 0;
        TimeSpan nextProgress = TimeSpan.Zero;
        TimeSpan lastProgress = TimeSpan.Zero;
        int lastPending = int.MaxValue;
        try
        {
            while (timer.Elapsed < TimeSpan.FromMinutes(10.0) && timer.Elapsed - lastProgress < TimeSpan.FromSeconds(120.0))
            {
                BridgeResponse bridgeResponse = await client.SendAsync("claimAchievements", new { maxClaims = 8 }, 15000, cancellationToken);
                if (!bridgeResponse.Ok)
                {
                    log("成就检查失败：" + bridgeResponse.Error);
                    int num = failures + 1;
                    failures = num;
                    if (num >= 3)
                    {
                        return AchievementClaimResult.Recover;
                    }

                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                if (ReadBool(bridgeResponse.Data, "unsafeToStart"))
                {
                    return AchievementClaimResult.Deferred;
                }

                int num2 = ReadInt(bridgeResponse.Data, "claimedCount");
                claimed += num2;
                submittedAny |= num2 > 0;
                dismissed += (ReadBool(bridgeResponse.Data, "popupDismissed") ? 1 : 0);
                if (ReadBool(bridgeResponse.Data, "questNoticeDismissed"))
                {
                    log("已关闭开局前任务提示，继续领取奖励。");
                }

                bool flag = ReadBool(bridgeResponse.Data, "cleanupPending");
                int num3 = ReadInt(bridgeResponse.Data, "pendingCount");
                if (num2 > 0 || num3 < lastPending || ReadBool(bridgeResponse.Data, "popupDismissed"))
                {
                    lastProgress = timer.Elapsed;
                }

                lastPending = num3;
                submittedAny |= flag || ReadBool(bridgeResponse.Data, "popupDismissed");
                if (num3 == 0 && !flag)
                {
                    emptyChecks++;
                    if (!submittedAny || emptyChecks >= 3)
                    {
                        log((claimed == 0) ? "成就检查完成：暂无未领取成就" : $"成就检查完成：无感领取 {claimed} 项，关闭奖励弹窗 {dismissed} 次");
                        return AchievementClaimResult.Ready;
                    }
                }
                else
                {
                    emptyChecks = 0;
                }

                if (num2 > 0)
                {
                    log($"成就领取已提交 {num2} 项，待处理 {num3} 项");
                }
                else if (timer.Elapsed >= nextProgress)
                {
                    nextProgress = timer.Elapsed + TimeSpan.FromSeconds(10.0);
                    log($"成就收尾：待领={num3}，等待回包={ReadInt(bridgeResponse.Data, "inFlightCount")}，奖励队列={ReadBool(bridgeResponse.Data, "rewardQueue")}，弹窗={ReadBool(bridgeResponse.Data, "showingReward")}，已关闭={dismissed}");
                    log($"奖励收尾状态：卷轴={ReadBool(bridgeResponse.Data, "rewardScrollVisible")}，置换={ReadBool(bridgeResponse.Data, "replacementPending")}，任务提示={ReadBool(bridgeResponse.Data, "questNoticeVisible")}，错误={ReadText(bridgeResponse.Data, "replacementError")}{ReadText(bridgeResponse.Data, "rewardScrollError")}{ReadText(bridgeResponse.Data, "questNoticeError")}");
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex2)
        {
            log("成就检查异常：" + ex2.Message);
            return AchievementClaimResult.Recover;
        }

        log("成就奖励尚未收尾，交由现有账号恢复流程处理");
        return AchievementClaimResult.Recover;
    }

    private static int ReadInt(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || !value.TryGetInt32(out var value2))
        {
            return 0;
        }

        return value2;
    }

    private static bool ReadBool(JsonElement data, string name)
    {
        if (data.TryGetProperty(name, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static string ReadText(JsonElement data, string name)
    {
        if (!data.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return value.GetString() ?? "";
    }
}