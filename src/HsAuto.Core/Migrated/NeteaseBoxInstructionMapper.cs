// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using HsAuto.Core.Models;

namespace HsAuto.Core.Strategy;
public static class NeteaseBoxInstructionMapper
{
    private sealed record StarshipLaunchOption(int OptionIndex, int SubOptionIndex, string CardId, string Error);
    private sealed record NeteaseCardReference(string CardId, string Name, int Position, int EntityId);
    internal const string TargetEntityIdsParameter = "netease.targetEntityIds";
    internal const string Jail319RerollParameter = "hsauto.jail319Reroll";
    internal const string Jail319ChoiceSignatureParameter = "hsauto.jail319ChoiceSignature";
    private const string Jail319RerollCardId = "JAIL_319t";
    public static bool TryMapFirst(ConstructedGameState state, NeteaseBoxRecommendation recommendation, out IReadOnlyList<ConstructedAction> actions, out string reason)
    {
        bool requiresTargetRefresh;
        return TryMapFirst(state, recommendation, out actions, out reason, out requiresTargetRefresh);
    }

    public static bool TryMapFirst(ConstructedGameState state, NeteaseBoxRecommendation recommendation, out IReadOnlyList<ConstructedAction> actions, out string reason, out bool requiresTargetRefresh, bool enableDirectCompatibility = false)
    {
        actions = Array.Empty<ConstructedAction>();
        reason = "";
        requiresTargetRefresh = false;
        NeteaseBoxRecommendedAction neteaseBoxRecommendedAction = recommendation.Data.FirstOrDefault();
        if ((object)neteaseBoxRecommendedAction == null || string.IsNullOrWhiteSpace(neteaseBoxRecommendedAction.ActionName))
        {
            reason = (string.IsNullOrWhiteSpace(recommendation.Error) ? "盒子AI尚未返回打法" : ("盒子AI未返回打法：" + recommendation.Error));
            return false;
        }

        string actionName = neteaseBoxRecommendedAction.ActionName.Trim().ToLowerInvariant();
        if (state.Phase == ConstructedPhase.Mulligan)
        {
            return TryMapMulligan(state, recommendation, neteaseBoxRecommendedAction, actionName, out actions, out reason);
        }

        ConstructedPhase phase = state.Phase;
        bool flag = (uint)(phase - 5) <= 1u;
        bool flag2 = flag;
        if (!flag2)
        {
            bool flag3;
            switch (actionName)
            {
                case "choice":
                case "choose":
                case "discard":
                    flag3 = true;
                    break;
                default:
                    flag3 = false;
                    break;
            }

            flag2 = flag3 && (state.ChoiceOptions.Count > 0 || state.DiscoverOptions.Count > 0);
        }

        if (flag2)
        {
            return TryMapChoice(state, recommendation, neteaseBoxRecommendedAction, out actions, out reason);
        }

        if (actionName == "end_turn")
        {
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(new ConstructedAction { Type = ConstructedActionType.EndTurn, Reason = "盒子AI首条打法要求结束回合" }, recommendation, neteaseBoxRecommendedAction));
            return true;
        }

        ConstructedEntity source = ResolveSource(state, neteaseBoxRecommendedAction, actionName, enableDirectCompatibility, out reason);
        if ((object)source == null)
        {
            return false;
        }

        if (actionName == "minion_attack" && ReadExplicitTargetReferences(neteaseBoxRecommendedAction).Count == 0 && !state.CurrentPlayableActions.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.Attack && string.Equals(candidate.SourceEntityId, source.EntityId, StringComparison.OrdinalIgnoreCase)) && TryReadStarshipLaunchOptions(state, source, out StarshipLaunchOption[] launchOptions))
        {
            return TryMapMisclassifiedStarshipAttack(state, recommendation, neteaseBoxRecommendedAction, source, launchOptions, out actions, out reason);
        }

        IReadOnlyList<ConstructedEntity> readOnlyList = Array.Empty<ConstructedEntity>();
        if (actionName != "trade")
        {
            readOnlyList = ResolveTargets(state, neteaseBoxRecommendedAction, source, actionName, enableDirectCompatibility, out reason);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return false;
            }
        }

        if (readOnlyList.Count > 1 && !IsSpecialInternalAction(actionName))
        {
            reason = $"盒子指令 {actionName} 给出了 {readOnlyList.Count} 个目标，但该动作不是客户端多选特殊操作";
            return false;
        }

        ConstructedEntity target = ((readOnlyList.Count == 1) ? readOnlyList[0] : null);
        ConstructedAction[] array = (
            from candidate in state.CurrentPlayableActions
            where string.Equals(candidate.SourceEntityId, source.EntityId, StringComparison.OrdinalIgnoreCase)
            where IsExpectedActionType(actionName, candidate.Type, enableDirectCompatibility)select candidate).ToArray();
        if (actionName == "play_minion" && (object)target != null && state.FriendlyBoard.Any((ConstructedEntity candidate) => string.Equals(candidate.EntityId, target.EntityId, StringComparison.OrdinalIgnoreCase)) && IsMagneticMinion(source))
        {
            int num = ReadZonePosition(target);
            ConstructedAction[] array2 = (
                from candidate in array
                where candidate.Type == ConstructedActionType.PlayCard
                where string.IsNullOrWhiteSpace(candidate.TargetEntityId)select candidate).ToArray();
            if (num <= 0 || array2.Length != 1)
            {
                ref string reference = ref reason;
                string text;
                if (num <= 0)
                {
                    text = "盒子磁力目标 " + Describe(target) + " 缺少有效棋盘位置";
                }
                else
                {
                    text = ((array2.Length == 0) ? ("盒子磁力指令未匹配到无目标客户端出牌动作：源 " + Describe(source) + "，目标 " + Describe(target)) : $"盒子磁力指令匹配到 {array2.Length} 个无目标客户端出牌动作，拒绝歧义执行");
                }

                reference = text;
                return false;
            }

            ConstructedAction constructedAction = Decorate(array2[0], recommendation, neteaseBoxRecommendedAction);
            Dictionary<string, string> parameters = new Dictionary<string, string>(constructedAction.Parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["netease.boardPosition"] = num.ToString(CultureInfo.InvariantCulture),
                ["netease.magneticTargetEntityId"] = target.EntityId
            };
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(constructedAction with { TargetEntityId = null, Parameters = parameters, Reason = "盒子AI要求将磁力随从 " + Describe(source) + " 贴到 " + Describe(target) });
            return true;
        }

        NeteaseCardReference subOptionReference = TryReadCardReference(neteaseBoxRecommendedAction.SubOption);
        if ((object)subOptionReference != null)
        {
            IEnumerable<ConstructedAction> second =
                from candidate in state.CurrentPlayableActions
                where candidate.Type == ConstructedActionType.InternalOption
                where string.Equals(candidate.SourceEntityId, source.EntityId, StringComparison.OrdinalIgnoreCase)select candidate;
            ConstructedAction[] array3 = (
                from candidate in array.Concat(second)
                where candidate.OptionIndex.HasValue
                where candidate.Parameters.Any((KeyValuePair<string, string> pair) => pair.Key.StartsWith("unity.subOption.", StringComparison.OrdinalIgnoreCase) && pair.Key.EndsWith(".cardId", StringComparison.OrdinalIgnoreCase) && CardIdsEqual(pair.Value, subOptionReference.CardId))select candidate).Distinct().ToArray();
            if (IsSpecialInternalAction(actionName))
            {
                array3 = PreferMatchingPowerKeyword(array3, actionName);
            }

            IGrouping<int, ConstructedAction>[] array4 = (
                from candidate in array3
                group candidate by candidate.OptionIndex.Value).ToArray();
            if (array4.Length != 1)
            {
                reason = ((array4.Length == 0) ? ("盒子指令 " + actionName + " 未匹配到包含子选项的客户端合法动作：源 " + Describe(source)) : $"盒子指令 {actionName} 的子选项跨越 {array4.Length} 个客户端 option，拒绝歧义执行");
                return false;
            }

            ConstructedAction? obj = array4[0].FirstOrDefault((ConstructedAction candidate) => candidate.Type == ConstructedActionType.InternalOption) ?? array4[0].First();
            if (!TryApplySubOption(obj with { Type = ConstructedActionType.InternalOption, TargetEntityId = null }, neteaseBoxRecommendedAction.SubOption, out ConstructedAction updated, out reason))
            {
                return false;
            }

            string text2 = updated.Parameters["unity.selectedSubOptionIndex"];
            string selectedTargetPrefix = "unity.subOption." + text2 + ".target.";
            HashSet<string> selectedLegalTargets = (
                from pair in updated.Parameters
                where pair.Key.StartsWith(selectedTargetPrefix, StringComparison.OrdinalIgnoreCase)select pair.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int result = 0;
            bool flag4 = updated.Parameters.TryGetValue("unity.subOption." + text2 + ".targetCount", out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            if (readOnlyList.Count == 0 && ((flag4 && result > 0) || (!flag4 && selectedLegalTargets.Count > 0)))
            {
                requiresTargetRefresh = true;
                reason = "盒子子选项 " + subOptionReference.CardId + " 需要目标，但当前缓存事件尚缺目标";
                return false;
            }

            if (((!IsSpecialInternalAction(actionName) && readOnlyList.Count > 0) & flag4) && result == 0)
            {
                reason = "盒子子选项 " + subOptionReference.CardId + " 是无目标分支，但推荐同时给出了目标";
                return false;
            }

            if (selectedLegalTargets.Count > 0 && readOnlyList.Any((ConstructedEntity candidate) => !selectedLegalTargets.Contains(candidate.EntityId)))
            {
                ConstructedEntity constructedEntity = readOnlyList.First((ConstructedEntity candidate) => !selectedLegalTargets.Contains(candidate.EntityId));
                reason = "盒子子选项不允许目标实体 " + constructedEntity.EntityId;
                return false;
            }

            Dictionary<string, string> dictionary = new Dictionary<string, string>(updated.Parameters, StringComparer.OrdinalIgnoreCase);
            if (readOnlyList.Count > 1)
            {
                dictionary["netease.targetEntityIds"] = string.Join(",", readOnlyList.Select((ConstructedEntity candidate) => candidate.EntityId));
            }

            updated = updated with
            {
                TargetEntityId = ((readOnlyList.Count == 1) ? readOnlyList[0].EntityId : null),
                Parameters = dictionary
            };
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(updated, recommendation, neteaseBoxRecommendedAction));
            return true;
        }

        ConstructedAction[] array5 = array.Where((ConstructedAction candidate) => ((object)target != null) ? string.Equals(candidate.TargetEntityId, target.EntityId, StringComparison.OrdinalIgnoreCase) : string.IsNullOrWhiteSpace(candidate.TargetEntityId)).ToArray();
        flag2 = (object)target == null;
        if (flag2)
        {
            string text3 = actionName;
            flag = ((text3 == "hero_attack" || text3 == "minion_attack") ? true : false);
            flag2 = flag;
        }

        if (flag2)
        {
            requiresTargetRefresh = true;
            reason = $"盒子指令 {actionName} 已给出源 {Describe(source)}，但当前缓存事件尚缺攻击目标";
            return false;
        }

        flag2 = (object)target == null && array5.Length == 0 && array.Length != 0 && array.All((ConstructedAction candidate) => !string.IsNullOrWhiteSpace(candidate.TargetEntityId));
        if (flag2)
        {
            switch (actionName)
            {
                case "play_minion":
                case "play_special":
                case "play_weapon":
                case "play_hero":
                case "play_location":
                    flag = true;
                    break;
                default:
                    flag = false;
                    break;
            }

            flag2 = !flag;
        }

        if (flag2)
        {
            requiresTargetRefresh = true;
            reason = $"盒子指令 {actionName} 已给出源 {Describe(source)}，但当前缓存事件尚缺目标";
            return false;
        }

        flag2 = (object)target == null && array5.Length == 0;
        if (flag2)
        {
            switch (actionName)
            {
                case "play_minion":
                case "play_special":
                case "play_weapon":
                case "play_hero":
                case "play_location":
                    flag = true;
                    break;
                default:
                    flag = false;
                    break;
            }

            flag2 = flag;
        }

        if (flag2 && array.Any((ConstructedAction candidate) => candidate.Type == ConstructedActionType.PlayCardWithTarget))
        {
            Dictionary<string, string> parameters2 = new Dictionary<string, string>(CopyEntityParameters(source), StringComparer.OrdinalIgnoreCase)
            {
                ["netease.selectSourceOnly"] = bool.TrueString
            };
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(new ConstructedAction { Type = ConstructedActionType.PlayCard, SourceEntityId = source.EntityId, Parameters = parameters2, ExpectedCost = source.Cost, Reason = "盒子AI先选择需指定目标的手牌 " + Describe(source) + "，等待给出目标" }, recommendation, neteaseBoxRecommendedAction));
            return true;
        }

        if (IsSpecialInternalAction(actionName))
        {
            array5 = PreferMatchingPowerKeyword(array5, actionName);
        }

        if (array5.Length != 1)
        {
            reason = ((array5.Length == 0) ? ("盒子指令 " + actionName + " 未匹配到客户端合法动作：源 " + Describe(source) + (((object)target == null) ? "" : ("，目标 " + Describe(target)))) : $"盒子指令 {actionName} 匹配到 {array5.Length} 个合法动作，拒绝歧义执行");
            return false;
        }

        ConstructedAction updated2 = Decorate(array5[0], recommendation, neteaseBoxRecommendedAction);
        if (!TryApplySubOption(updated2, neteaseBoxRecommendedAction.SubOption, out updated2, out reason))
        {
            return false;
        }

        actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(updated2);
        return true;
    }

    private static bool TryMapMulligan(ConstructedGameState state, NeteaseBoxRecommendation recommendation, NeteaseBoxRecommendedAction instruction, string actionName, out IReadOnlyList<ConstructedAction> actions, out string reason)
    {
        actions = Array.Empty<ConstructedAction>();
        reason = "";
        if (actionName != "replace")
        {
            reason = "盒子AI起手阶段返回了未支持指令 " + actionName;
            return false;
        }

        List<ConstructedAction> list = new List<ConstructedAction>();
        IReadOnlyList<NeteaseCardReference> readOnlyList = ReadCardReferences(instruction.Card);
        HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NeteaseCardReference item in readOnlyList)
        {
            ConstructedEntity constructedEntity = ResolveUniqueEntity(state.Hand, item, hashSet, out reason);
            if ((object)constructedEntity == null)
            {
                return false;
            }

            hashSet.Add(constructedEntity.EntityId);
            list.Add(Decorate(new ConstructedAction { Type = ConstructedActionType.MulliganReplace, SourceEntityId = constructedEntity.EntityId, Parameters = CopyEntityParameters(constructedEntity), Reason = "盒子AI要求替换起手 " + Describe(constructedEntity) }, recommendation, instruction));
        }

        list.Add(Decorate(new ConstructedAction { Type = ConstructedActionType.ConfirmMulligan, Reason = ((readOnlyList.Count == 0) ? "盒子AI要求保留全部起手牌并确认" : "盒子AI起手替换选择完成后确认") }, recommendation, instruction));
        actions = list;
        return true;
    }

    private static bool TryMapChoice(ConstructedGameState state, NeteaseBoxRecommendation recommendation, NeteaseBoxRecommendedAction instruction, out IReadOnlyList<ConstructedAction> actions, out string reason)
    {
        actions = Array.Empty<ConstructedAction>();
        reason = "";
        IReadOnlyList<ConstructedEntity> readOnlyList = ((state.ChoiceOptions.Count > 0) ? state.ChoiceOptions : state.DiscoverOptions);
        int num = ReadStateIntTag(state, "unity.targetChoiceCountMin");
        int num2 = ReadStateIntTag(state, "unity.targetChoiceCountMax");
        NeteaseCardReference[] array = recommendation.Data.SelectMany(ReadExplicitTargetReferences).ToArray();
        if (num > 1)
        {
            List<ConstructedEntity> list = new List<ConstructedEntity>();
            HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            NeteaseCardReference[] array2 = array;
            foreach (NeteaseCardReference reference in array2)
            {
                ConstructedEntity constructedEntity = ResolveUniqueEntity(readOnlyList, reference, hashSet, out reason);
                if ((object)constructedEntity == null)
                {
                    return false;
                }

                hashSet.Add(constructedEntity.EntityId);
                list.Add(constructedEntity);
            }

            if (list.Count < num || (num2 > 0 && list.Count > num2))
            {
                reason = $"盒子AI给出 {list.Count} 个选择目标，客户端要求 {num} 到 {num2} 个";
                return false;
            }

            if (list.Count < 2)
            {
                reason = "盒子AI多选指令没有提供足够的目标";
                return false;
            }

            ConstructedEntity firstOption = list[0];
            int item = readOnlyList.Select((ConstructedEntity entity, int index) => (entity: entity, index: index)).First(((ConstructedEntity entity, int index) tuple) => string.Equals(tuple.entity.EntityId, firstOption.EntityId, StringComparison.OrdinalIgnoreCase)).index;
            Dictionary<string, string> parameters = new Dictionary<string, string>(CopyEntityParameters(firstOption), StringComparer.OrdinalIgnoreCase)
            {
                ["netease.targetEntityIds"] = string.Join(",", list.Select((ConstructedEntity candidate) => candidate.EntityId))
            };
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(new ConstructedAction { Type = ConstructedActionType.ChooseOption, SourceEntityId = firstOption.EntityId, OptionIndex = item, Parameters = parameters, Reason = $"盒子AI要求同时选择 {list.Count} 个目标：{string.Join("、", list.Select(Describe))}" }, recommendation, instruction));
            return true;
        }

        NeteaseCardReference neteaseCardReference = TryReadCardReference(instruction.SubOption) ?? TryReadCardReference(instruction.Target) ?? TryReadCardReference(instruction.OppTarget) ?? ReadCardReferences(instruction.Card).FirstOrDefault();
        if ((object)neteaseCardReference == null)
        {
            reason = "盒子AI选择指令没有卡牌 ID 或位置";
            return false;
        }

        if (string.Equals(neteaseCardReference.CardId, "JAIL_319t", StringComparison.OrdinalIgnoreCase) && neteaseCardReference.Position == readOnlyList.Count + 1 && neteaseCardReference.Position == 4 && neteaseCardReference.EntityId <= 0)
        {
            Dictionary<string, string> parameters2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["hsauto.jail319Reroll"] = bool.TrueString,
                ["hsauto.jail319ChoiceSignature"] = BuildChoiceSignature(readOnlyList),
                ["cardId"] = "JAIL_319t",
                ["name"] = (string.IsNullOrWhiteSpace(neteaseCardReference.Name) ? "钥匙变形！" : neteaseCardReference.Name)
            };
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(new ConstructedAction { Type = ConstructedActionType.ChooseOption, OptionIndex = 3, Parameters = parameters2, Reason = "盒子AI要求使用万能钥匙第 4 项刷新当前发现选项" }, recommendation, instruction));
            return true;
        }

        ConstructedEntity option = ResolveUniqueChoiceEntity(readOnlyList, neteaseCardReference, out reason);
        if ((object)option == null)
        {
            return false;
        }

        int item2 = readOnlyList.Select((ConstructedEntity entity, int index) => (entity: entity, index: index)).First(((ConstructedEntity entity, int index) tuple) => string.Equals(tuple.entity.EntityId, option.EntityId, StringComparison.OrdinalIgnoreCase)).index;
        actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(new ConstructedAction { Type = ConstructedActionType.ChooseOption, SourceEntityId = option.EntityId, OptionIndex = item2, Parameters = CopyEntityParameters(option), Reason = $"盒子AI要求选择第 {item2 + 1} 项 {Describe(option)}" }, recommendation, instruction));
        return true;
    }

    private static string BuildChoiceSignature(IReadOnlyList<ConstructedEntity> options)
    {
        return string.Join("|", options.Select((ConstructedEntity option) => option.EntityId + ":" + option.CardId));
    }

    private static ConstructedEntity? ResolveUniqueChoiceEntity(IReadOnlyList<ConstructedEntity> options, NeteaseCardReference reference, out string reason)
    {
        if (reference.EntityId > 0)
        {
            ConstructedEntity constructedEntity = options.FirstOrDefault((ConstructedEntity option) => string.Equals(option.EntityId, reference.EntityId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase));
            if ((object)constructedEntity != null)
            {
                reason = "";
                return constructedEntity;
            }
        }

        if (reference.Position > 0 && reference.Position <= options.Count)
        {
            ConstructedEntity constructedEntity2 = options[reference.Position - 1];
            if (string.IsNullOrWhiteSpace(reference.CardId) || CardIdsEqual(constructedEntity2.CardId, reference.CardId))
            {
                reason = "";
                return constructedEntity2;
            }
        }

        ConstructedEntity[] array = options.Where((ConstructedEntity option) => string.IsNullOrWhiteSpace(reference.CardId) || CardIdsEqual(option.CardId, reference.CardId)).ToArray();
        if (array.Length == 1)
        {
            reason = "";
            return array[0];
        }

        return ResolveUniqueEntity(options, reference, null, out reason);
    }

    private static ConstructedEntity? ResolveSource(ConstructedGameState state, NeteaseBoxRecommendedAction instruction, string actionName, bool enableDirectCompatibility, out string reason)
    {
        reason = "";
        if (actionName == "hero_attack")
        {
            return ResolveFixedSource(state.LocalHero, instruction.Card, "盒子要求英雄攻击，但我方英雄实体不存在", "我方英雄", allowHeroFamilyForSkin: true, out reason);
        }

        if (actionName == "hero_skill")
        {
            return ResolveHeroPowerSource(state, instruction.Card, out reason);
        }

        NeteaseCardReference reference = TryReadCardReference(instruction.Card);
        if ((object)reference == null)
        {
            reason = "盒子指令 " + actionName + " 缺少源卡牌 ID/位置";
            return null;
        }

        IEnumerable<ConstructedEntity> source;
        switch (actionName)
        {
            case "play_minion":
            case "play_weapon":
            case "forge":
            case "trade":
            case "discard":
            case "prepare":
            case "play_special":
            case "play_hero":
            case "play_location":
                source = state.Hand;
                break;
            default:
                source = state.FriendlyBoard.Concat(((object)state.LocalHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { state.LocalHero }).Concat(state.GetLocalHeroPowers());
                break;
        }

        ConstructedEntity[] array = source.ToArray();
        HashSet<string> legalSourceIds = (
            from candidate in state.CurrentPlayableActions
            where IsExpectedActionType(actionName, candidate.Type, enableDirectCompatibility)select candidate.SourceEntityId into entityId
                where !string.IsNullOrWhiteSpace(entityId)select entityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ConstructedEntity[] array2 = (
            from candidate in array.Where((ConstructedEntity entity) => Matches(entity, reference)).ToArray()
            where legalSourceIds.Contains(candidate.EntityId)select candidate).ToArray();
        if (array2.Length == 1)
        {
            return array2[0];
        }

        bool flag;
        switch (actionName)
        {
            case "play_minion":
            case "play_weapon":
            case "forge":
            case "trade":
            case "discard":
            case "prepare":
            case "play_special":
            case "play_hero":
            case "play_location":
                flag = true;
                break;
            default:
                flag = false;
                break;
        }

        bool flag2 = flag || (enableDirectCompatibility && actionName == "location_power");
        if ((reference.EntityId > 0 && legalSourceIds.Contains(reference.EntityId.ToString(CultureInfo.InvariantCulture))) & flag2)
        {
            reason = "";
            ConstructedEntity constructedEntity = new ConstructedEntity
            {
                EntityId = reference.EntityId.ToString(CultureInfo.InvariantCulture),
                CardId = reference.CardId,
                Zone = ((!(actionName == "location_power")) ? ConstructedZone.Hand : ConstructedZone.FriendlyBoard),
                IsMinion = (actionName == "play_minion")
            };
            string text = actionName;
            flag = ((text == "play_special" || text == "prepare") ? true : false);
            constructedEntity = constructedEntity with
            {
                IsSpell = flag
            };
            constructedEntity = constructedEntity with
            {
                IsWeapon = actionName == "play_weapon"
            };
            text = actionName;
            bool isLocation = ((text == "play_location" || text == "location_power") ? true : false);
            constructedEntity = constructedEntity with
            {
                IsLocation = isLocation
            };
            constructedEntity = constructedEntity with
            {
                Tags = ((reference.Position > 0) ? new Dictionary<string, string>
                {
                    ["unity.zonePos"] = reference.Position.ToString(CultureInfo.InvariantCulture)
                }

                : new Dictionary<string, string>())
            };
            return constructedEntity;
        }

        ConstructedEntity constructedEntity4 = null;
        if (reference.Position > 0 && reference.Position <= array.Length)
        {
            ConstructedEntity constructedEntity5 = array[reference.Position - 1];
            if (string.IsNullOrWhiteSpace(reference.CardId) || CardIdsEqual(constructedEntity5.CardId, reference.CardId))
            {
                constructedEntity4 = constructedEntity5;
                if (legalSourceIds.Contains(constructedEntity5.EntityId))
                {
                    return constructedEntity5;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(reference.CardId))
        {
            ConstructedEntity[] array3 = (
                from candidate in array
                where CardIdsEqual(candidate.CardId, reference.CardId)
                where legalSourceIds.Contains(candidate.EntityId)select candidate).ToArray();
            if (array3.Length == 1)
            {
                return array3[0];
            }
        }

        if ((object)constructedEntity4 != null)
        {
            return constructedEntity4;
        }

        return ResolveUniqueEntity(array, reference, null, out reason);
    }

    private static ConstructedEntity? ResolveHeroPowerSource(ConstructedGameState state, JsonElement element, out string reason)
    {
        reason = "";
        IReadOnlyList<ConstructedEntity> localHeroPowers = state.GetLocalHeroPowers();
        if (localHeroPowers.Count == 0)
        {
            reason = "盒子要求英雄技能，但我方英雄技能实体不存在";
            return null;
        }

        HashSet<string> legalSourceIds = (
            from candidate in state.CurrentPlayableActions.Where((ConstructedAction candidate) =>
            {
                ConstructedActionType type = candidate.Type;
                return (uint)(type - 8) <= 1u;
            })select candidate.SourceEntityId into entityId
                where !string.IsNullOrWhiteSpace(entityId)select entityId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        NeteaseCardReference reference = TryReadCardReference(element);
        if ((object)reference == null)
        {
            ConstructedEntity[] array = localHeroPowers.Where((ConstructedEntity candidate) => legalSourceIds.Contains(candidate.EntityId)).ToArray();
            object obj;
            if (array.Length != 1)
            {
                obj = state.LocalHeroPower;
                if (obj == null)
                {
                    return localHeroPowers[0];
                }
            }
            else
            {
                obj = array[0];
            }

            return (ConstructedEntity? )obj;
        }

        ConstructedEntity[] array2 = localHeroPowers.Where((ConstructedEntity candidate) => Matches(candidate, reference)).ToArray();
        ConstructedEntity[] array3 = array2.Where((ConstructedEntity candidate) => legalSourceIds.Contains(candidate.EntityId)).ToArray();
        if (array3.Length == 1)
        {
            return array3[0];
        }

        if (array2.Length == 1)
        {
            return array2[0];
        }

        ConstructedEntity[] array4 = localHeroPowers.Where((ConstructedEntity candidate) => MatchesHero(candidate, reference)).ToArray();
        ConstructedEntity[] array5 = array4.Where((ConstructedEntity candidate) => legalSourceIds.Contains(candidate.EntityId)).ToArray();
        if (array5.Length == 1)
        {
            return array5[0];
        }

        if (array4.Length == 1)
        {
            return array4[0];
        }

        string value = string.Join("、", localHeroPowers.Select(Describe));
        reason = $"盒子我方英雄技能源 {reference.CardId}/位置{reference.Position} 与客户端英雄技能 [{value}] 不一致";
        return null;
    }

    private static ConstructedEntity? ResolveFixedSource(ConstructedEntity? entity, JsonElement element, string missingReason, string label, bool allowHeroFamilyForSkin, out string reason)
    {
        reason = "";
        if ((object)entity == null)
        {
            reason = missingReason;
            return null;
        }

        NeteaseCardReference neteaseCardReference = TryReadCardReference(element);
        if ((object)neteaseCardReference != null && !(allowHeroFamilyForSkin ? MatchesHero(entity, neteaseCardReference) : Matches(entity, neteaseCardReference)))
        {
            reason = $"盒子{label}源 {neteaseCardReference.CardId}/位置{neteaseCardReference.Position} 与客户端 {Describe(entity)} 不一致";
            return null;
        }

        return entity;
    }

    private static IReadOnlyList<ConstructedEntity> ResolveTargets(ConstructedGameState state, NeteaseBoxRecommendedAction instruction, ConstructedEntity source, string actionName, bool enableDirectCompatibility, out string reason)
    {
        reason = "";
        if (IsSpecialInternalAction(actionName) && IsRepeatedSourceTarget(instruction, source))
        {
            return Array.Empty<ConstructedEntity>();
        }

        HashSet<string> legalTargetIds = (
            from candidate in state.CurrentPlayableActions
            where IsExpectedActionType(actionName, candidate.Type, enableDirectCompatibility)
            where string.Equals(candidate.SourceEntityId, source.EntityId, StringComparison.OrdinalIgnoreCase)select candidate.TargetEntityId into entityId
                where !string.IsNullOrWhiteSpace(entityId)select (entityId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        JsonElement element = FirstTargetMarker(instruction.OppTargetHero, instruction.HyphenOppTargetHero);
        if (HasTargetMarker(element))
        {
            ConstructedEntity constructedEntity = MatchHero(state.OpponentHero, element, "敌方英雄", out reason);
            if ((object)constructedEntity != null)
            {
                return new ConstructedEntity[1]
                {
                    constructedEntity
                };
            }

            return Array.Empty<ConstructedEntity>();
        }

        JsonElement element2 = FirstTargetMarker(instruction.TargetHero, instruction.HyphenTargetHero);
        if (HasTargetMarker(element2))
        {
            ConstructedEntity constructedEntity2 = MatchHero(state.LocalHero, element2, "我方英雄", out reason);
            if ((object)constructedEntity2 != null)
            {
                return new ConstructedEntity[1]
                {
                    constructedEntity2
                };
            }

            return Array.Empty<ConstructedEntity>();
        }

        IReadOnlyList<NeteaseCardReference> readOnlyList = ReadCardReferences(FirstTargetMarker(instruction.OppTarget, instruction.HyphenOppTarget));
        if (readOnlyList.Count > 0)
        {
            IReadOnlyList<ConstructedEntity> result = ResolveUniqueLegalTargets(state.EnemyBoard, readOnlyList, legalTargetIds, enableDirectCompatibility, out reason);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return Array.Empty<ConstructedEntity>();
            }

            return result;
        }

        IReadOnlyList<NeteaseCardReference> readOnlyList2 = ReadCardReferences(FirstTargetMarker(instruction.Target, instruction.HyphenTarget));
        if (readOnlyList2.Count > 0)
        {
            IReadOnlyList<ConstructedEntity> result2 = ResolveUniqueLegalTargets(state.FriendlyBoard.Concat(state.Hand), readOnlyList2, legalTargetIds, enableDirectCompatibility, out reason);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return Array.Empty<ConstructedEntity>();
            }

            return result2;
        }

        return Array.Empty<ConstructedEntity>();
    }

    private static IReadOnlyList<ConstructedEntity> ResolveUniqueLegalTargets(IEnumerable<ConstructedEntity> entities, IReadOnlyList<NeteaseCardReference> references, ISet<string> legalTargetIds, bool enableDirectCompatibility, out string reason)
    {
        reason = "";
        ConstructedEntity[] source = entities.ToArray();
        List<ConstructedEntity> list = new List<ConstructedEntity>();
        HashSet<string> usedEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NeteaseCardReference reference in references)
        {
            ConstructedEntity constructedEntity = ResolveUniqueLegalTarget(source.Where((ConstructedEntity candidate) => !usedEntityIds.Contains(candidate.EntityId)), reference, legalTargetIds, enableDirectCompatibility, out reason);
            if ((object)constructedEntity == null)
            {
                return Array.Empty<ConstructedEntity>();
            }

            usedEntityIds.Add(constructedEntity.EntityId);
            list.Add(constructedEntity);
        }

        return list;
    }

    private static bool IsRepeatedSourceTarget(NeteaseBoxRecommendedAction instruction, ConstructedEntity source)
    {
        return new JsonElement[2]
        {
            FirstTargetMarker(instruction.OppTarget, instruction.HyphenOppTarget),
            FirstTargetMarker(instruction.Target, instruction.HyphenTarget)
        }.Any((JsonElement marker) => ReadCardReferences(marker).Any((NeteaseCardReference reference) => Matches(source, reference)));
    }

    private static IReadOnlyList<NeteaseCardReference> ReadExplicitTargetReferences(NeteaseBoxRecommendedAction instruction)
    {
        return new JsonElement[4]
        {
            FirstTargetMarker(instruction.OppTargetHero, instruction.HyphenOppTargetHero),
            FirstTargetMarker(instruction.TargetHero, instruction.HyphenTargetHero),
            FirstTargetMarker(instruction.OppTarget, instruction.HyphenOppTarget),
            FirstTargetMarker(instruction.Target, instruction.HyphenTarget)
        }.SelectMany(ReadCardReferences).ToArray();
    }

    private static int ReadStateIntTag(ConstructedGameState state, string key)
    {
        if (!state.Tags.TryGetValue(key, out string value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }

    private static ConstructedEntity? ResolveUniqueLegalTarget(IEnumerable<ConstructedEntity> entities, NeteaseCardReference reference, ISet<string> legalTargetIds, bool enableDirectCompatibility, out string reason)
    {
        ConstructedEntity[] array = entities.Where((ConstructedEntity entity) => Matches(entity, reference)).ToArray();
        if (array.Length > 1)
        {
            ConstructedEntity[] array2 = array.Where((ConstructedEntity candidate) => legalTargetIds.Contains(candidate.EntityId)).ToArray();
            if (array2.Length == 1)
            {
                reason = "";
                return array2[0];
            }
        }

        if (enableDirectCompatibility && reference.EntityId > 0 && legalTargetIds.Contains(reference.EntityId.ToString(CultureInfo.InvariantCulture)))
        {
            reason = "";
            return new ConstructedEntity
            {
                EntityId = reference.EntityId.ToString(CultureInfo.InvariantCulture),
                CardId = reference.CardId
            };
        }

        return ResolveUniqueEntity(array, reference, null, out reason);
    }

    private static JsonElement FirstTargetMarker(JsonElement primary, JsonElement alternate)
    {
        if (!HasTargetMarker(primary))
        {
            return alternate;
        }

        return primary;
    }

    private static bool HasTargetMarker(JsonElement element)
    {
        long value;
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().Any(),
            JsonValueKind.Array => element.GetArrayLength() > 0,
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Number => !element.TryGetInt64(out value) || value != 0,
            JsonValueKind.True => true,
            _ => false,
        };
    }

    private static ConstructedEntity? MatchHero(ConstructedEntity? hero, JsonElement element, string side, out string reason)
    {
        reason = "";
        if ((object)hero == null)
        {
            reason = "盒子目标是" + side + "，但客户端英雄实体不存在";
            return null;
        }

        NeteaseCardReference neteaseCardReference = TryReadCardReference(element);
        if ((object)neteaseCardReference != null && !MatchesHero(hero, neteaseCardReference))
        {
            reason = $"盒子{side}目标 {neteaseCardReference.CardId} 与客户端 {hero.CardId} 不一致";
            return null;
        }

        return hero;
    }

    private static ConstructedEntity? ResolveUniqueEntity(IEnumerable<ConstructedEntity> entities, NeteaseCardReference reference, ISet<string>? excludedEntityIds, out string reason)
    {
        ConstructedEntity[] array = (
            from entity in entities
            where excludedEntityIds == null || !excludedEntityIds.Contains(entity.EntityId)
            where Matches(entity, reference)select entity).ToArray();
        if (array.Length == 1)
        {
            reason = "";
            return array[0];
        }

        string text = $"entityId={reference.EntityId}, cardId={reference.CardId}, position={reference.Position}";
        reason = ((array.Length == 0) ? ("未找到盒子卡牌 " + text + " 对应的客户端实体") : $"盒子卡牌 {text} 对应到 {array.Length} 个客户端实体，拒绝歧义执行");
        return null;
    }

    private static bool Matches(ConstructedEntity entity, NeteaseCardReference reference)
    {
        if (reference.EntityId > 0)
        {
            return string.Equals(entity.EntityId, reference.EntityId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(reference.CardId) && !CardIdsEqual(entity.CardId, reference.CardId))
        {
            return false;
        }

        if (reference.Position > 0 && ReadZonePosition(entity) != reference.Position)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(reference.CardId))
        {
            return reference.Position > 0;
        }

        return true;
    }

    private static bool MatchesHero(ConstructedEntity hero, NeteaseCardReference reference)
    {
        if (reference.EntityId > 0)
        {
            return string.Equals(hero.EntityId, reference.EntityId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(reference.CardId) && !HeroCardIdsEqual(hero.CardId, reference.CardId))
        {
            return false;
        }

        if (reference.Position > 0 && ReadZonePosition(hero) != reference.Position)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(reference.CardId))
        {
            return reference.Position > 0;
        }

        return true;
    }

    private static int ReadZonePosition(ConstructedEntity entity)
    {
        if (!entity.Tags.TryGetValue("unity.zonePos", out string value) || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }

    private static bool IsMagneticMinion(ConstructedEntity entity)
    {
        if (entity.IsMinion)
        {
            if (!entity.Mechanics.Any((string mechanic) => string.Equals(mechanic, "MAGNETIC", StringComparison.OrdinalIgnoreCase) || string.Equals(mechanic, "MODULAR", StringComparison.OrdinalIgnoreCase)) && !entity.ReferencedTags.Any((string tag) => string.Equals(tag, "MAGNETIC", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "MODULAR", StringComparison.OrdinalIgnoreCase)) && !ReadBooleanTag(entity.Tags, "MAGNETIC"))
            {
                return ReadBooleanTag(entity.Tags, "MODULAR");
            }

            return true;
        }

        return false;
    }

    private static bool ReadBooleanTag(IReadOnlyDictionary<string, string> tags, string name)
    {
        if (!tags.TryGetValue(name, out string value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var result))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2))
            {
                return result2 != 0;
            }

            return false;
        }

        return result;
    }

    private static bool CardIdsEqual(string left, string right)
    {
        return string.Equals(NormalizeCardId(left), NormalizeCardId(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HeroCardIdsEqual(string left, string right)
    {
        string text = NormalizeCardId(left);
        string text2 = NormalizeCardId(right);
        if (string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string text3 = TryReadHeroFamily(text);
        string b = TryReadHeroFamily(text2);
        if (text3 != null)
        {
            return string.Equals(text3, b, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string NormalizeCardId(string value)
    {
        string text = value.Trim();
        if (!text.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        string text2 = text;
        return text2.Substring(5, text2.Length - 5);
    }

    private static string? TryReadHeroFamily(string cardId)
    {
        if (cardId.Length >= 7 && cardId.StartsWith("HERO_", StringComparison.OrdinalIgnoreCase))
        {
            char c = cardId[5];
            if (c >= '0' && c <= '9')
            {
                c = cardId[6];
                if (c >= '0' && c <= '9')
                {
                    return cardId.Substring(0, 7);
                }
            }
        }

        return null;
    }

    private static bool IsExpectedActionType(string actionName, ConstructedActionType type, bool enableDirectCompatibility = false)
    {
        switch (actionName)
        {
            case "play_minion":
            case "play_weapon":
            case "play_location":
            case "play_special":
            case "play_hero":
                return (uint)(type - 5) <= 1u;
            case "trade":
                return type == ConstructedActionType.TradeCard;
            case "hero_attack":
            case "minion_attack":
                return type == ConstructedActionType.Attack;
            case "hero_skill":
                return (uint)(type - 8) <= 1u;
            case "location_power":
            {
                bool flag = (uint)(type - 10) <= 1u;
                bool flag2 = flag;
                if (!flag2)
                {
                    bool flag3 = enableDirectCompatibility;
                    if (flag3)
                    {
                        bool flag4 = (uint)(type - 5) <= 1u;
                        flag3 = flag4;
                    }

                    flag2 = flag3;
                }

                return flag2;
            }

            case "titan_power":
            case "common_action":
            case "forge":
            case "discard":
            case "prepare":
            case "launch_starship":
                return type == ConstructedActionType.InternalOption;
            default:
                return false;
        }
    }

    private static bool IsSpecialInternalAction(string actionName)
    {
        switch (actionName)
        {
            case "forge":
            case "prepare":
            case "discard":
            case "titan_power":
            case "launch_starship":
            case "common_action":
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadStarshipLaunchOptions(ConstructedGameState state, ConstructedEntity source, out StarshipLaunchOption[] launchOptions)
    {
        launchOptions = Array.Empty<StarshipLaunchOption>();
        if (!int.TryParse(source.EntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceEntityId) || sourceEntityId <= 0)
        {
            return false;
        }

        launchOptions = state.PredictorOptions.Where((ConstructedPredictorOption option) => option.EntityId == sourceEntityId).SelectMany((ConstructedPredictorOption option) =>
            from subOption in option.SubOptions
            where CardIdsEqual(subOption.CardId, "GDB_905")select new StarshipLaunchOption(option.Id, subOption.Id, subOption.CardId, subOption.Error)).Distinct().ToArray();
        return launchOptions.Length != 0;
    }

    private static bool TryMapMisclassifiedStarshipAttack(ConstructedGameState state, NeteaseBoxRecommendation recommendation, NeteaseBoxRecommendedAction instruction, ConstructedEntity source, IReadOnlyList<StarshipLaunchOption> launchOptions, out IReadOnlyList<ConstructedAction> actions, out string reason)
    {
        actions = Array.Empty<ConstructedAction>();
        reason = "";
        StarshipLaunchOption[] array = launchOptions.Where((StarshipLaunchOption option) => string.Equals(option.Error, "NONE", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (array.Length != 0)
        {
            (ConstructedAction, StarshipLaunchOption)[] array2 = array.SelectMany((StarshipLaunchOption launchOption) =>
                from candidate in state.CurrentPlayableActions
                where candidate.Type == ConstructedActionType.InternalOption && candidate.OptionIndex == launchOption.OptionIndex && string.Equals(candidate.SourceEntityId, source.EntityId, StringComparison.OrdinalIgnoreCase)
                where candidate.Parameters.TryGetValue($"unity.subOption.{launchOption.SubOptionIndex}.cardId", out string value) && CardIdsEqual(value, launchOption.CardId)select (Action: candidate, Launch: launchOption)).ToArray();
            if (array2.Length != 1)
            {
                reason = ((array2.Length == 0) ? ("盒子将未发射星舰 " + Describe(source) + " 误标为无目标攻击，但客户端合法发射动作尚未完成投影") : $"盒子将未发射星舰 {Describe(source)} 误标为无目标攻击，客户端存在 {array2.Length} 个发射动作，拒绝歧义执行");
                return false;
            }

            (ConstructedAction, StarshipLaunchOption) tuple = array2[0];
            Dictionary<string, string> parameters = new Dictionary<string, string>(tuple.Item1.Parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["unity.selectedSubOptionIndex"] = tuple.Item2.SubOptionIndex.ToString(CultureInfo.InvariantCulture),
                ["netease.subOptionCardId"] = tuple.Item2.CardId
            };
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(tuple.Item1 with { Type = ConstructedActionType.InternalOption, TargetEntityId = null, Parameters = parameters, Reason = "盒子将未发射星舰 " + Describe(source) + " 误标为无目标攻击，已按客户端合法发射子选项执行" }, recommendation, instruction));
            return true;
        }

        if (launchOptions.All((StarshipLaunchOption option) => option.Error.Contains("REQ_ENOUGH_MANA", StringComparison.OrdinalIgnoreCase)))
        {
            actions = new _003C_003Ez__ReadOnlySingleElementList<ConstructedAction>(Decorate(new ConstructedAction { Type = ConstructedActionType.EndTurn, Reason = "盒子将费用不足的未发射星舰 " + Describe(source) + " 误标为无目标攻击，结束回合避免持续等待" }, recommendation, instruction));
            return true;
        }

        reason = "盒子将未发射星舰 " + Describe(source) + " 误标为无目标攻击，但发射子选项当前不可用：" + string.Join("、", launchOptions.Select((StarshipLaunchOption option) => option.Error));
        return false;
    }

    private static ConstructedAction[] PreferMatchingPowerKeyword(ConstructedAction[] candidates, string actionName)
    {
        string[] tokens = actionName switch
        {
            "forge" => new string[1]
            {
                "FORGE"
            },
            "prepare" => new string[1]
            {
                "PREPARE"
            },
            "titan_power" => new string[1]
            {
                "TITAN"
            },
            "launch_starship" => new string[2]
            {
                "STARSHIP",
                "LAUNCH"
            },
            _ => Array.Empty<string>(),
        };
        if (tokens.Length == 0)
        {
            return candidates;
        }

        ConstructedAction[] array = candidates.Where((ConstructedAction candidate) => candidate.Parameters.Any((KeyValuePair<string, string> pair) => pair.Key.Contains("powerKeyword", StringComparison.OrdinalIgnoreCase) && tokens.Any((string token) => pair.Value.Contains(token, StringComparison.OrdinalIgnoreCase)))).ToArray();
        if (array.Length == 0)
        {
            return candidates;
        }

        return array;
    }

    private static bool TryApplySubOption(ConstructedAction action, JsonElement subOptionElement, out ConstructedAction updated, out string reason)
    {
        updated = action;
        reason = "";
        NeteaseCardReference reference = TryReadCardReference(subOptionElement);
        if ((object)reference == null || string.IsNullOrWhiteSpace(reference.CardId))
        {
            return true;
        }

        string[] array = (
            from pair in action.Parameters
            where pair.Key.EndsWith(".cardId", StringComparison.OrdinalIgnoreCase) && pair.Key.StartsWith("unity.subOption.", StringComparison.OrdinalIgnoreCase) && CardIdsEqual(pair.Value, reference.CardId)select pair.Key.Split('.')[2] into index
                where reference.Position <= 0 || (action.Parameters.TryGetValue("unity.subOption." + index + ".zonePosition", out string value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result2) && result2 == reference.Position)select index).Distinct(StringComparer.Ordinal).ToArray();
        if (array.Length != 1 || !int.TryParse(array[0], out var result))
        {
            reason = ((array.Length == 0) ? ("盒子要求子选项 " + reference.CardId + "，但当前 OptionsPacket 未提供该子选项") : ("盒子子选项 " + reference.CardId + " 对应到多个 OptionsPacket 项"));
            return false;
        }

        Dictionary<string, string> parameters = new Dictionary<string, string>(action.Parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["unity.selectedSubOptionIndex"] = result.ToString(CultureInfo.InvariantCulture),
            ["netease.subOptionCardId"] = reference.CardId
        };
        if (!string.IsNullOrWhiteSpace(action.TargetEntityId))
        {
            string targetPrefix = $"unity.subOption.{result}.target.";
            HashSet<string> hashSet = (
                from pair in action.Parameters
                where pair.Key.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase)select pair.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (hashSet.Count > 0 && !hashSet.Contains(action.TargetEntityId))
            {
                reason = "盒子子选项 " + reference.CardId + " 不允许目标实体 " + action.TargetEntityId;
                return false;
            }
        }

        updated = action with
        {
            Parameters = parameters
        };
        return true;
    }

    private static ConstructedAction Decorate(ConstructedAction action, NeteaseBoxRecommendation recommendation, NeteaseBoxRecommendedAction instruction)
    {
        Dictionary<string, string> dictionary = new Dictionary<string, string>(action.Parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["netease.optionId"] = recommendation.OptionId.ToString(CultureInfo.InvariantCulture),
            ["netease.choiceId"] = recommendation.ChoiceId.ToString(CultureInfo.InvariantCulture),
            ["netease.turnNum"] = recommendation.TurnNum.ToString(CultureInfo.InvariantCulture),
            ["netease.actionName"] = instruction.ActionName,
            ["netease.usingDisguised"] = instruction.UsingDisguised.ToString(CultureInfo.InvariantCulture)
        };
        if (instruction.Position > 0)
        {
            dictionary["netease.boardPosition"] = instruction.Position.ToString(CultureInfo.InvariantCulture);
        }

        return action with
        {
            Parameters = dictionary,
            Reason = (string.IsNullOrWhiteSpace(action.Reason) ? ("盒子AI首条打法：" + instruction.ActionName) : action.Reason)
        };
    }

    private static IReadOnlyDictionary<string, string> CopyEntityParameters(ConstructedEntity entity)
    {
        return new Dictionary<string, string>(entity.Tags, StringComparer.OrdinalIgnoreCase)
        {
            ["cardId"] = entity.CardId,
            ["name"] = entity.Name
        };
    }

    private static IReadOnlyList<NeteaseCardReference> ReadCardReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return (
                from reference in element.EnumerateArray().Select(TryReadCardReference)
                where (object)reference != null
                select (reference)).ToArray();
        }

        NeteaseCardReference neteaseCardReference = TryReadCardReference(element);
        if ((object)neteaseCardReference != null)
        {
            return new NeteaseCardReference[1]
            {
                neteaseCardReference
            };
        }

        return Array.Empty<NeteaseCardReference>();
    }

    private static NeteaseCardReference? TryReadCardReference(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string text = ReadString(element, "cardId") ?? ReadString(element, "CardID") ?? "";
        string name = ReadString(element, "cardName") ?? ReadString(element, "name") ?? "";
        int num = ReadInt(element, "ZONE_POSITION");
        if (num <= 0)
        {
            num = ReadInt(element, "position");
        }

        int num2 = ReadInt(element, "ENTITY_ID");
        if (num2 <= 0)
        {
            num2 = ReadInt(element, "entity_id");
        }

        if (!string.IsNullOrWhiteSpace(text) || num > 0 || num2 > 0)
        {
            return new NeteaseCardReference(text, name, num, num2);
        }

        return null;
    }

    private static bool HasObject(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            return element.EnumerateObject().Any();
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static int ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
        {
            return value2;
        }

        if (value.ValueKind != JsonValueKind.String || !int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value2))
        {
            return 0;
        }

        return value2;
    }

    private static ConstructedEntity? Fail(string message, out string reason)
    {
        reason = message;
        return null;
    }

    private static string Describe(ConstructedEntity entity)
    {
        return $"{entity.Name}({entity.CardId},实体{entity.EntityId},位置{ReadZonePosition(entity)})";
    }
}