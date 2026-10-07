// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Collections.Generic;
using System.Linq;

namespace HsAuto.Core.Models;
public static class ConstructedActionRules
{
    public static bool IsTargetAllowed(ConstructedGameState state, ConstructedActionType actionType, ConstructedEntity? source, ConstructedEntity? target, out string reason)
    {
        reason = "";
        if ((object)target == null)
        {
            return true;
        }

        bool flag = target.IsLocation;
        if (flag)
        {
            bool flag2 = (((uint)(actionType - 6) <= 1u || actionType == ConstructedActionType.UseHeroPowerWithTarget) ? true : false);
            flag = flag2;
        }

        if (flag)
        {
            reason = "目标 " + DisplayName(target) + " 是地标，不能按普通随从目标处理";
            return false;
        }

        if (IsEnemyStealthMinion(target))
        {
            reason = "目标 " + DisplayName(target) + " 当前处于潜行，不能成为我方攻击、出牌或英雄技能目标";
            return false;
        }

        if (actionType == ConstructedActionType.Attack && TargetableEnemyTaunts(state).ToArray().Length != 0 && (target.Zone != ConstructedZone.EnemyBoard || !target.HasTaunt))
        {
            reason = "敌方有嘲讽，必须先攻击可被攻击的嘲讽随从";
            return false;
        }

        if (IsSpellOrHeroPowerTargeting(actionType, source) && IsSpellOrHeroPowerProtected(target))
        {
            reason = "目标 " + DisplayName(target) + " 具有扰魔/无法成为法术或英雄技能目标，不能被该动作指定";
            return false;
        }

        return true;
    }

    public static IEnumerable<ConstructedEntity> TargetableEnemyBoard(ConstructedGameState state)
    {
        return state.EnemyBoard.Where((ConstructedEntity card) => !card.IsLocation && !IsEnemyStealthMinion(card));
    }

    public static IEnumerable<ConstructedEntity> TargetableEnemyTaunts(ConstructedGameState state)
    {
        return
            from card in TargetableEnemyBoard(state)
            where card.HasTaunt
            select card;
    }

    public static bool IsEnemyStealthMinion(ConstructedEntity entity)
    {
        if (entity.Zone == ConstructedZone.EnemyBoard && entity.IsMinion)
        {
            if (!entity.HasStealth)
            {
                return IsEntityTagTrue(entity, "stealth");
            }

            return true;
        }

        return false;
    }

    public static bool IsSpellOrHeroPowerProtected(ConstructedEntity entity)
    {
        if (!entity.CantBeTargetedBySpellsOrHeroPowers && !IsEntityTagTrue(entity, "cantBeTargetedBySpells") && !IsEntityTagTrue(entity, "cantBeTargetedByHeroPowers") && !IsEntityTagTrue(entity, "cantBeTargetedByAbilities") && !IsEntityTagTrue(entity, "cantBeTargetedByOpponentAbilities") && !IsEntityTagTrue(entity, "cantBeTargetedByOpponents"))
        {
            return IsEntityTagTrue(entity, "elusive");
        }

        return true;
    }

    private static bool IsSpellOrHeroPowerTargeting(ConstructedActionType actionType, ConstructedEntity? source)
    {
        return actionType switch
        {
            ConstructedActionType.PlayCardWithTarget => source?.IsSpell ?? false,
            ConstructedActionType.UseHeroPowerWithTarget => true,
            _ => false,
        };
    }

    private static bool IsEntityTagTrue(ConstructedEntity entity, string key)
    {
        string value;
        bool result = default;
        return (entity.Tags.TryGetValue(key, out value) && bool.TryParse(value, out result)) & result;
    }

    private static string DisplayName(ConstructedEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.Name))
        {
            return entity.Name;
        }

        return entity.EntityId;
    }
}