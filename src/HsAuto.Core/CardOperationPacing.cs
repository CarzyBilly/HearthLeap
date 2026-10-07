using System.Globalization;
using HsAuto.Core.Models;
using HsAuto.Open;

namespace HsAuto.Core.Automation;

/// <summary>Controller-side pacing; never sleeps the Unity or UI thread.</summary>
public sealed class CardOperationPacing
{
    public const int MinimumDelayMs = 1000;
    public const int MaximumDelayMs = 1800;
    private readonly Func<int, int, int> _next;
    private readonly Func<int, CancellationToken, Task> _delay;

    // Injectable randomness/waiting keeps offline tests deterministic and fast.
    public CardOperationPacing(Func<int, int, int> next = null,
        Func<int, CancellationToken, Task> delay = null)
    {
        _next = next ?? ((min, exclusiveMax) => Random.Shared.Next(min, exclusiveMax));
        _delay = delay ?? ((ms, token) => Task.Delay(ms, token));
    }

    public static bool NeedsDelay(ConstructedActionType type) => type is
        ConstructedActionType.MulliganReplace or
        ConstructedActionType.PlayCard or ConstructedActionType.PlayCardWithTarget or
        ConstructedActionType.Attack or
        ConstructedActionType.UseHeroPower or ConstructedActionType.UseHeroPowerWithTarget or
        ConstructedActionType.UseLocation or ConstructedActionType.UseLocationWithTarget or
        ConstructedActionType.TradeCard or ConstructedActionType.InternalOption;

    // Choice/target confirmation and emergency cancellation stay part of the current
    // operation. Delaying them would split one card operation into separate steps.
    public async Task<bool> WaitAsync(ConstructedActionType type, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!NeedsDelay(type)) return false;
        int milliseconds = _next(MinimumDelayMs, MaximumDelayMs + 1);
        if (milliseconds < MinimumDelayMs || milliseconds > MaximumDelayMs)
            throw new InvalidOperationException("卡牌等待时间超出 1000～1800 毫秒范围。");
        await _delay(milliseconds, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return true;
    }

    public static bool MatchesDecision(ConstructedDecisionEnvelope decision, ConstructedGameState state) =>
        StateSafety.IsReadable(state) && string.Equals(decision.MatchId, state.MatchId, StringComparison.Ordinal);

    public static bool CanReuseAfterWait(ConstructedGameState before, ConstructedGameState after, ConstructedAction action)
    {
        if (!StateSafety.IsReadable(before) || !StateSafety.IsReadable(after) ||
            before.MatchId != after.MatchId || before.Turn != after.Turn ||
            before.Phase != after.Phase || before.Result != after.Result) return false;

        // A new packet means a new recommendation is needed even if the source card
        // still exists. Never rebind an old recommendation to a different packet.
        if (before.Phase == ConstructedPhase.LocalTurn)
        {
            var beforePacket = before.Tags.GetValueOrDefault("unity.optionsPacketId", "");
            var afterPacket = after.Tags.GetValueOrDefault("unity.optionsPacketId", "");
            if (beforePacket != afterPacket) return false;
            if (action.Parameters.TryGetValue("netease.optionId", out var expected) &&
                (!int.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int expectedId) ||
                 !int.TryParse(afterPacket, NumberStyles.Integer, CultureInfo.InvariantCulture, out int actualId) ||
                 expectedId <= 0 || expectedId != actualId)) return false;
        }
        return true;
    }
}
