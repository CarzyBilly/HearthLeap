// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Automation;
public sealed class ConstructedDeckNotFoundException : Exception
{
    public string DeckName { get; }

    public ConstructedDeckNotFoundException(string deckName) : base("进入传统对战后未找到卡组“" + deckName.Trim() + "”。请检查输入的卡组名称是否与游戏内完全一致；当前账号脚本已自动停止。")
    {
        DeckName = deckName.Trim();
    }
}