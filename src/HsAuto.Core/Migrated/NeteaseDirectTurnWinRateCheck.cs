// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Bot;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;

namespace HsAuto.Core.Strategy;
internal sealed class NeteaseDirectTurnWinRateCheck
{
    internal const string StateParameter = "hsauto.ai2WinRateState";
    internal static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(900.0);
    private readonly ConstructedTurnStartGate _drawGate = new ConstructedTurnStartGate();
    private string _supplementedTurn = "";
    private string _lastLoggedResult = "";
    internal static bool IsEligible(ConstructedGameState state, ConstructedSettings settings)
    {
        if (settings.EnableNeteaseBoxLowWinRateAutoConcede && !settings.DryRun && state.Phase == ConstructedPhase.LocalTurn)
        {
            return (state.Turn + 1) / 2 >= 4;
        }

        return false;
    }

    internal ConstructedAction? ObserveDraw(ConstructedGameState state, ConstructedSettings settings)
    {
        ConstructedTurnStartGateCheck constructedTurnStartGateCheck = _drawGate.Check(state, settings.EnableNeteaseBoxLowWinRateAutoConcede);
        ConstructedPhase phase = state.Phase;
        if (((uint)(phase - 1) <= 1u || phase == ConstructedPhase.GameOver) ? true : false)
        {
            _supplementedTurn = "";
            _lastLoggedResult = "";
        }

        if (!IsEligible(state, settings) || constructedTurnStartGateCheck.CanProceed)
        {
            return null;
        }

        return new ConstructedAction
        {
            Type = ConstructedActionType.Wait,
            Reason = "盒子AI 等待右侧抽牌完成后检查胜率"
        };
    }

    internal async Task<ConstructedAction?> CheckAsync(ConstructedGameState state, ConstructedSettings settings, double? recommendedPercent, Func<CancellationToken, Task<double?>> fetch, Action<string> log, CancellationToken cancellationToken)
    {
        if (!IsEligible(state, settings))
        {
            return null;
        }

        string text = $"{state.MatchId}|{state.Turn}";
        Stopwatch timer = Stopwatch.StartNew();
        double? percent = recommendedPercent;
        double valueOrDefault = default;
        int num;
        if (percent.HasValue)
        {
            valueOrDefault = percent.GetValueOrDefault();
            num = ((!double.IsFinite(valueOrDefault)) ? 1 : 0);
        }
        else
        {
            num = 1;
        }

        bool flag = (byte)num != 0;
        if (!flag)
        {
            bool flag2 = ((valueOrDefault < 0.0 || valueOrDefault > 100.0) ? true : false);
            flag = flag2;
        }

        if (flag)
        {
            if (_supplementedTurn == text || ConstructedHumanizedPacingScheduler.IsRopeActive(state))
            {
                return null;
            }

            _supplementedTurn = text;
            using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(Budget);
            try
            {
                percent = await fetch(budget.Token).WaitAsync(budget.Token).ConfigureAwait(continueOnCapturedContext: false);
            }
            catch (OperationCanceledException)when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex2)when (!(ex2 is OperationCanceledException))
            {
                log("盒子AI 胜率补充未就绪：" + ex2.GetType().Name + "。");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        double value = default;
        int num2;
        if (percent.HasValue)
        {
            value = percent.GetValueOrDefault();
            num2 = ((!double.IsFinite(value)) ? 1 : 0);
        }
        else
        {
            num2 = 1;
        }

        flag = (byte)num2 != 0;
        if (!flag)
        {
            bool flag2 = ((value < 0.0 || value > 100.0) ? true : false);
            flag = flag2;
        }

        if (flag)
        {
            log($"盒子AI 胜率补充获取失败或超时，耗时 {timer.Elapsed.TotalMilliseconds:0}ms；继续正常打牌，本回合仅检查后续推荐附带胜率。");
            return null;
        }

        string text2 = $"{StateKey(state)}|{value:R}";
        if (_lastLoggedResult != text2)
        {
            _lastLoggedResult = text2;
            log($"盒子AI 我方第 {(state.Turn + 1) / 2} 回合胜率：{value:0.00}%，阈值 {settings.NeteaseBoxLowWinRateConcedeThresholdPercent}%，补充检查耗时 {timer.Elapsed.TotalMilliseconds:0}ms。");
        }

        if (value >= (double)settings.NeteaseBoxLowWinRateConcedeThresholdPercent)
        {
            return null;
        }

        return new ConstructedAction
        {
            Type = ConstructedActionType.Concede,
            Reason = $"盒子AI 我方回合预测胜率 {value:0.00}% 低于 {settings.NeteaseBoxLowWinRateConcedeThresholdPercent}%",
            Parameters = new Dictionary<string, string>
            {
                ["hsauto.automaticConcede"] = "true",
                ["hsauto.automaticConcedeReason"] = "NeteaseDirectLowWinRate",
                ["hsauto.ai2WinRateState"] = StateKey(state)
            }
        };
    }

    internal static string StateKey(ConstructedGameState state)
    {
        HashCode hashCode = default;
        hashCode.Add(state.MatchId);
        hashCode.Add(state.Turn);
        hashCode.Add(state.Phase);
        hashCode.Add(state.Tags.GetValueOrDefault("unity.optionsPacketId", ""));
        hashCode.Add(state.ManaAvailable);
        hashCode.Add(state.ManaTotal);
        hashCode.Add(state.HeroHealth);
        hashCode.Add(state.HeroArmor);
        hashCode.Add(state.OpponentHealth);
        hashCode.Add(state.OpponentArmor);
        foreach (ConstructedEntity item in state.Hand.Concat(state.FriendlyBoard).Concat(state.EnemyBoard))
        {
            hashCode.Add(item.EntityId);
            hashCode.Add(item.CardId);
            hashCode.Add(item.Attack);
            hashCode.Add(item.Health);
            hashCode.Add(item.Tags.GetValueOrDefault("unity.zonePos", ""));
        }

        return hashCode.ToHashCode().ToString(CultureInfo.InvariantCulture);
    }
}