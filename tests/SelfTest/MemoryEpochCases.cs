using System.Reflection;
using HsAuto.Core.Automation;
using HsAuto.Core.Strategy;

/// <summary>
/// Offline cache-bookkeeping tests for recommendation epochs. These invoke only
/// private in-memory bookkeeping; they never initialize a source or scan a process.
/// </summary>
public static class MemoryEpochCases
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("相同无时间戳 payload 有新来源证据时跨新对局可重新读取", () =>
        {
            var source = NewSource();
            var raw = UntimestampedRecommendation();
            Cache(source, raw);
            DateTimeOffset firstSeen = Normalize(source, raw).CapturedAt;
            DateTimeOffset epoch = After(firstSeen);

            source.SetRecommendationEpoch(epoch);
            WaitUntil(epoch);
            // The payload bytes are identical, but a changed callback address is
            // the only producer/source evidence available to this offline model.
            // This represents a fresh callback rather than unchanged old memory.
            Cache(source, raw with { Address = raw.Address + 0x1000 });

            var current = CurrentMatches(source, raw.Payload.OptionId);
            Need(current.Count == 1 && current[0].CapturedAt >= epoch,
                "跨局后具有新来源证据的相同未标时 payload 没有重新进入当前推荐窗口");
            return Task.CompletedTask;
        });

        await check("安全回归：未变化的旧无时间戳建议不得因新 epoch 被复活", () =>
        {
            var source = NewSource();
            var raw = UntimestampedRecommendation();
            Cache(source, raw);
            DateTimeOffset oldSyntheticTime = Normalize(source, raw).CapturedAt;
            DateTimeOffset epoch = After(oldSyntheticTime);

            source.SetRecommendationEpoch(epoch);
            WaitUntil(epoch);
            Cache(source, raw); // Identical old bytes remain in memory; no producer generation exists.

            Need(CurrentMatches(source, raw.Payload.OptionId).Count == 0,
                "回归：SetRecommendationEpoch 清掉首次时间后，把未变化的旧内存重新标成当前时间并返回");
            return Task.CompletedTask;
        });

        await check("显式 callback 时间戳跨 epoch 始终原样保留且旧事件仍被过滤", () =>
        {
            var source = NewSource();
            DateTimeOffset capturedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            var raw = UntimestampedRecommendation() with { CapturedAt = capturedAt };

            var before = Normalize(source, raw);
            source.SetRecommendationEpoch(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
            var after = Normalize(source, raw);
            Cache(source, raw);

            Need(before.CapturedAt == capturedAt && after.CapturedAt == capturedAt,
                "来源提供的显式时间戳被改写");
            Need(CurrentMatches(source, raw.Payload.OptionId).Count == 0,
                "早于 epoch 的显式时间戳事件仍出现在当前推荐窗口");
            return Task.CompletedTask;
        });

        await check("读取器重定位不刷新旧合成时间", () =>
        {
            var source = NewSource();
            var raw = UntimestampedRecommendation();
            Cache(source, raw);
            var before = Normalize(source, raw);
            var rendererIds = (HashSet<int>)Field(source, "_rendererProcessIds").GetValue(source)!;
            rendererIds.Add(raw.RendererProcessId);

            Method(source, "InvalidateRendererLocations").Invoke(source, null);
            var after = Normalize(source, raw);

            Need(before.CapturedAt == after.CapturedAt,
                "InvalidateRendererLocations 之后旧 payload 的合成首次时间被刷新");
            Need(rendererIds.Count == 0, "重定位夹具未清理 renderer 位置缓存");
            Need(CurrentMatches(source, raw.Payload.OptionId).Single().CapturedAt == before.CapturedAt,
                "仅重定位后缓存中的旧建议发生变化");
            return Task.CompletedTask;
        });
    }

    private static NeteaseBoxRecommendationObservation UntimestampedRecommendation() => new(
        new NeteaseBoxRecommendation
        {
            OptionId = 731,
            ChoiceId = 19,
            TurnNum = 2,
            Status = 0,
            Data = [new NeteaseBoxRecommendedAction { ActionName = "end_turn" }]
        },
        DateTimeOffset.MinValue,
        RendererProcessId: 101,
        Address: 0x1000);

    private static void Cache(NeteaseBoxRecommendationSource source, NeteaseBoxRecommendationObservation raw) =>
        Method(source, "CacheObservations").Invoke(source, [new[] { raw }]);

    private static NeteaseBoxRecommendationObservation Normalize(
        NeteaseBoxRecommendationSource source, NeteaseBoxRecommendationObservation raw) =>
        (NeteaseBoxRecommendationObservation)Method(source, "NormalizeMemoryObservation").Invoke(source, [raw])!;

    private static IReadOnlyList<NeteaseBoxRecommendationObservation> CurrentMatches(
        NeteaseBoxRecommendationSource source, int optionId) =>
        (IReadOnlyList<NeteaseBoxRecommendationObservation>)Method(source, "CurrentMatches").Invoke(source, [optionId])!;

    private static MethodInfo Method(NeteaseBoxRecommendationSource source, string name) =>
        source.GetType().GetMethod(name, PrivateInstance)
        ?? throw new MissingMethodException(source.GetType().FullName, name);

    private static FieldInfo Field(NeteaseBoxRecommendationSource source, string name) =>
        source.GetType().GetField(name, PrivateInstance)
        ?? throw new MissingFieldException(source.GetType().FullName, name);

    private static NeteaseBoxRecommendationSource NewSource()
    {
        var source = new NeteaseBoxRecommendationSource();
        // CurrentMatches subtracts a two-second window from this field. The
        // production object sets it during InitializeAsync; these tests stay
        // offline, so seed only that in-memory boundary via reflection.
        Field(source, "_initializedAt").SetValue(source, DateTimeOffset.UtcNow.AddMinutes(-1));
        return source;
    }

    private static DateTimeOffset After(DateTimeOffset value)
    {
        DateTimeOffset epoch = value.AddTicks(1);
        return epoch > DateTimeOffset.UtcNow ? epoch : DateTimeOffset.UtcNow;
    }

    private static void WaitUntil(DateTimeOffset epoch)
    {
        while (DateTimeOffset.UtcNow < epoch)
        {
            Thread.Yield();
        }
    }

    private static void Need(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
