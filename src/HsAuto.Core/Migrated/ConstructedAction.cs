// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Collections.Generic;

namespace HsAuto.Core.Models;
public sealed record ConstructedAction
{
    public ConstructedActionType Type { get; init; }
    public string? SourceEntityId { get; init; }
    public string? TargetEntityId { get; init; }
    public int? OptionIndex { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string Reason { get; init; } = "";
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public int? ExpectedCost { get; init; }
    public string AtomicSequenceId { get; init; } = "";
    public int AtomicStepIndex { get; init; }
    public IReadOnlyDictionary<string, double> ScoreBreakdown { get; init; } = new Dictionary<string, double>();

    public override string ToString()
    {
        string text = (string.IsNullOrWhiteSpace(SourceEntityId) ? "" : (" " + SourceEntityId));
        string text2 = (string.IsNullOrWhiteSpace(TargetEntityId) ? "" : ("，目标 " + TargetEntityId));
        string text3 = (string.IsNullOrWhiteSpace(Reason) ? "" : ("：" + Reason));
        return ActionTypeLabel(Type) + text + text2 + text3;
    }

    private static string ActionTypeLabel(ConstructedActionType type)
    {
        return type switch
        {
            ConstructedActionType.NoOp => "无操作",
            ConstructedActionType.Wait => "等待",
            ConstructedActionType.MulliganKeep => "起手保留",
            ConstructedActionType.MulliganReplace => "起手换掉",
            ConstructedActionType.ConfirmMulligan => "确认起手",
            ConstructedActionType.PlayCard => "打出手牌",
            ConstructedActionType.PlayCardWithTarget => "指定目标出牌",
            ConstructedActionType.Attack => "攻击",
            ConstructedActionType.UseHeroPower => "使用英雄技能",
            ConstructedActionType.UseHeroPowerWithTarget => "指定目标英雄技能",
            ConstructedActionType.UseLocation => "激活地标",
            ConstructedActionType.UseLocationWithTarget => "指定目标激活地标",
            ConstructedActionType.TradeCard => "交易卡牌",
            ConstructedActionType.InternalOption => "执行客户端合法特殊操作",
            ConstructedActionType.ChooseOption => "选择选项",
            ConstructedActionType.EndTurn => "结束回合",
            ConstructedActionType.CancelPendingInput => "取消待定输入",
            ConstructedActionType.Concede => "认输",
            _ => type.ToString(),
        };
    }
}