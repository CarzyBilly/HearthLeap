// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Cards;
using HsAuto.Core.Models;

namespace HsAuto.Core.Automation;
public sealed class UnityBridgeConstructedGameStateReader
{
    private sealed record UnityCard(string Name, string EntityId, int EntityNumber, string CardId, string UnityZone, int ZonePos, int PlayerId, string Path, int InstanceId, bool IsSpell, bool IsWeapon, bool IsHeroPower, bool IsMinion, bool IsHero, bool? Tradeable, bool? HasResponse, bool? IsValidOption, bool? Taunt, bool? DivineShield, bool? Rush, bool? Charge, bool? Stealth, bool? CantBeTargetedBySpells, bool? CantBeTargetedByHeroPowers, bool? CantBeTargetedByAbilities, bool? CantBeTargetedByOpponentAbilities, bool? CantBeTargetedByOpponents, bool? Immune, string? CardText, string? PlayError, int Attack, int Health, int Damage, int Armor, int Durability, int LocationCooldown, int Cost);
    private sealed record UnityConstructedOption(int Index, int Type, string TypeName, int MainEntityId, int MainPlayerId, bool IsTrade, string PowerKeyword, string Error, bool MainIsValid, bool HasTargets, IReadOnlyList<int> MainValidTargetEntityIds, IReadOnlyList<int> ValidTargetEntityIds, IReadOnlyList<UnityConstructedSubOption> SubOptions);
    private sealed record UnityConstructedSubOption(int Index, int EntityId, string CardId, int ZonePosition, string PowerKeyword, string Error, IReadOnlyList<int> ValidTargetEntityIds);
    private static readonly Regex CardObjectNamePattern = new Regex("^(?<name>.*)\\s\\[id=(?<id>\\d+) cardId=(?<cardId>[^ ]+) zone=(?<zone>[^ ]+) zonePos=(?<zonePos>\\d+) player=(?<player>\\d+)\\]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const int EmptyChoicePromptGraceMs = 3500;
    private readonly UnityBridgeClient _client;
    private readonly HearthstoneCardDatabase _cardDatabase;
    private readonly bool _useNativeTradeTags;
    private ConstructedFormat _format;
    private long _sequence;
    private ConstructedPhase _lastPhase;
    private string _lastHandSignature = "";
    private string _lastBoardSignature = "";
    private string _lastActionSignature = "";
    private string _lastChoiceSignature = "";
    private string _emptyChoicePromptKey = "";
    private DateTimeOffset _phaseStableSince = DateTimeOffset.MinValue;
    private DateTimeOffset _handStableSince = DateTimeOffset.MinValue;
    private DateTimeOffset _boardStableSince = DateTimeOffset.MinValue;
    private DateTimeOffset _actionStableSince = DateTimeOffset.MinValue;
    private DateTimeOffset _choiceStableSince = DateTimeOffset.MinValue;
    private DateTimeOffset _emptyChoicePromptSince = DateTimeOffset.MinValue;
    private int _emptyChoicePromptAgeMs;
    private bool _emptyChoicePromptIgnored;
    private string _sessionMatchId = "";
    private bool _nextMatchPending;
    private int _highestMatchTurn;
    public int CardDatabaseCount => _cardDatabase.Count;
    public string CardDatabaseSourcePath => _cardDatabase.SourcePath;
    public string CardDatabaseLoadError => _cardDatabase.LoadError;

    public event Action<string>? Log;
    public UnityBridgeConstructedGameStateReader(UnityBridgeClient? client = null, ConstructedFormat format = ConstructedFormat.Standard, HearthstoneCardDatabase? cardDatabase = null, bool useNativeTradeTags = false)
    {
        _client = client ?? new UnityBridgeClient();
        _format = format;
        _cardDatabase = cardDatabase ?? HearthstoneCardDatabase.Default;
        _useNativeTradeTags = useNativeTradeTags;
    }

    public void SetFormat(ConstructedFormat format)
    {
        _format = format;
    }

    public async Task<UnityBridgeConstructedObservedState> ReadAsync(CancellationToken cancellationToken)
    {
        BridgeResponse bridgeResponse;
        try
        {
            object arguments;
            if (_useNativeTradeTags)
            {
                arguments = new
                {
                    maxCards = 2200,
                    mode = "Constructed",
                    nativeTradeTags = true,
                    ai3Diagnostics = _client.CaptureStateReadDetails
                };
            }
            else
            {
                arguments = (_client.CaptureStateReadDetails ? ((object)new
                {
                    maxCards = 2200,
                    mode = "Constructed",
                    ai3Diagnostics = true
                }

                ) : ((object)new
                {
                    maxCards = 2200,
                    mode = "Constructed"
                }

                ));
            }

            bridgeResponse = await _client.SendAsync("battlegroundsStateLite", arguments, 10000, cancellationToken);
        }
        catch (Exception ex)when (!(ex is OperationCanceledException))
        {
            Log?.Invoke("传统局面读取失败：" + ex.Message);
            return new UnityBridgeConstructedObservedState(++_sequence, BuildUnknownState("bridge-exception:" + ex.Message));
        }

        if (!bridgeResponse.Ok)
        {
            Log?.Invoke("传统局面读取失败：" + (bridgeResponse.Error ?? "UnityBridge 未返回成功"));
            return new UnityBridgeConstructedObservedState(++_sequence, BuildUnknownState(bridgeResponse.Error ?? "bridge-failed"));
        }

        try
        {
            return new UnityBridgeConstructedObservedState(++_sequence, BuildStateFromLite(bridgeResponse.Data));
        }
        catch (Exception ex2)when (!(ex2 is OperationCanceledException))
        {
            Log?.Invoke("传统局面解析失败：" + ex2.Message);
            return new UnityBridgeConstructedObservedState(++_sequence, BuildUnknownState("parse-exception:" + ex2.Message));
        }
    }

    private ConstructedGameState BuildStateFromLite(JsonElement data)
    {
        List<UnityCard> cards = ReadCardsFromProperty(data, "objects");
        List<UnityCard> list = ReadCardsFromProperty(data, "choiceCards");
        IReadOnlyList<int> source = ReadIntArray(data, "friendlyTargetChoiceEntityIds");
        IReadOnlyList<UnityConstructedOption> readOnlyList = ReadConstructedOptions(data);
        int num = ReadNestedInt(data, "turnOwner", "friendlyPlayerId", 0);
        int num2 = ReadNestedInt(data, "turnOwner", "opposingPlayerId", 0);
        int localPlayerId = ((num > 0) ? num : DetectLocalPlayerId(cards));
        int opponentPlayerId = ((num2 > 0) ? num2 : DetectOpponentPlayerId(cards, localPlayerId));
        ConstructedEntity[] array = (
            from card in (
                from card in cards
                where card.PlayerId == localPlayerId
                where IsUnityZone(card, "HAND")select card).Where(IsGameplayCard)orderby card.ZonePos, card.EntityNumber
            select ToEntity(card, ConstructedZone.Hand)).ToArray();
        ConstructedEntity[] array2 = (
            from card in (
                from card in cards
                where card.PlayerId == localPlayerId
                where IsUnityZone(card, "PLAY")select card).Where(IsBoardEntity)orderby card.ZonePos, card.EntityNumber
            select ToEntity(card, ConstructedZone.FriendlyBoard, IsAttackOption(card))).ToArray();
        ConstructedEntity[] array3 = (
            from card in (
                from card in cards
                where opponentPlayerId == 0 || card.PlayerId == opponentPlayerId || card.PlayerId != localPlayerId
                where IsUnityZone(card, "PLAY")select card).Where(IsBoardEntity)orderby card.ZonePos, card.EntityNumber
            select ToEntity(card, ConstructedZone.EnemyBoard)).ToArray();
        int secretsCount = cards.Count((UnityCard card) => card.PlayerId != localPlayerId && (opponentPlayerId == 0 || card.PlayerId == opponentPlayerId) && IsUnityZone(card, "SECRET"));
        ConstructedEntity constructedEntity = (
            from card in (
                from card in cards
                where card.PlayerId == localPlayerId
                where IsUnityZone(card, "PLAY")select card).Where(IsHero)orderby card.ZonePos
            select ToEntity(card, ConstructedZone.FriendlyHero)).FirstOrDefault();
        ConstructedEntity constructedEntity2 = (
            from card in (
                from card in cards
                where card.PlayerId != localPlayerId
                where IsUnityZone(card, "PLAY")select card).Where(IsHero)orderby card.ZonePos
            select ToEntity(card, ConstructedZone.EnemyHero)).FirstOrDefault();
        ConstructedEntity friendlyWeapon = (
            from card in (
                from card in cards
                where card.PlayerId == localPlayerId
                where IsUnityZone(card, "PLAY")select card).Where(IsWeapon)orderby card.ZonePos
            select ToEntity(card, ConstructedZone.FriendlyWeapon)).FirstOrDefault();
        ConstructedEntity enemyWeapon = (
            from card in (
                from card in cards
                where card.PlayerId != localPlayerId
                where IsUnityZone(card, "PLAY")select card).Where(IsWeapon)orderby card.ZonePos
            select ToEntity(card, ConstructedZone.EnemyWeapon)).FirstOrDefault();
        ConstructedEntity[] array4 = (
            from card in (
                from card in cards
                where card.PlayerId == localPlayerId
                where IsUnityZone(card, "PLAY") || IsUnityZone(card, "HAND")select card).Where(IsHeroPower)orderby card.ZonePos, card.EntityNumber
            select ToEntity(card, ConstructedZone.FriendlyHero)).ToArray();
        ConstructedEntity constructedEntity3 = array4.FirstOrDefault();
        ConstructedEntity[] array5 = (
            from card in list.Where(IsGameplayCard)orderby card.ZonePos, card.EntityNumber
            select ToEntity(card, ConstructedZone.Discover)).ToArray();
        ConstructedEntity[] array6 = (
            from entityId in source
            select cards.FirstOrDefault((UnityCard card) => card.EntityNumber == entityId)into card
                where (object)card != null
                select ToEntity(card, ConstructedZone.Choice)).ToArray();
        ConstructedEntity[] array7 = ((array5.Length != 0) ? array5.Select((ConstructedEntity entity) => entity with { Zone = ConstructedZone.Choice }).ToArray() : array6);
        int num3 = Math.Clamp(ReadNestedInt(data, "mana", "ready", 0), 0, 99);
        int val = ReadNestedInt(data, "mana", "total", num3);
        int val2 = ReadNestedInt(data, "turnStart", "maxResources", 0);
        int manaTotal = Math.Clamp(Math.Max(Math.Max(val, val2), num3), 0, 99);
        ConstructedPhase phase = DetectPhase(data, cards, array7.Length, array.Length, array2.Length, array3.Length);
        ConstructedGameResult result = ParseConstructedResult(GetString(data, "constructedResult"));
        IReadOnlyList<ConstructedAction> readOnlyList2 = BuildCurrentPlayableActions(array, array2, constructedEntity, array4, constructedEntity2, array3, num3, phase, readOnlyList);
        array = MarkInternallyConfirmedPlayableEntities(array, readOnlyList2);
        array4 = MarkInternallyConfirmedPlayableEntities(array4, readOnlyList2);
        constructedEntity3 = array4.FirstOrDefault();
        StateStability stability = UpdateStability(phase, BuildEntityListSignature(array), BuildBoardSignature(constructedEntity, constructedEntity2, array2, array3, friendlyWeapon, enemyWeapon, array4), BuildActionListSignature(readOnlyList2), BuildEntityListSignature(array7));
        IReadOnlyDictionary<string, string> tags = BuildStateTags(data, localPlayerId, opponentPlayerId, cards.Count, list.Count, stability);
        return new ConstructedGameState
        {
            MatchId = ResolveMatchId(phase, GetInt(data, "gameTurn")),
            Turn = ResolveTurn(data, manaTotal),
            Phase = phase,
            Format = _format,
            Result = result,
            LocalHero = constructedEntity,
            OpponentHero = constructedEntity2,
            LocalPlayerClass = ReadClass(constructedEntity),
            OpponentClass = ReadClass(constructedEntity2),
            ManaAvailable = num3,
            ManaTotal = manaTotal,
            HeroHealth = (((object)constructedEntity != null && constructedEntity.Health > 0) ? constructedEntity.Health : 30),
            HeroArmor = ReadIntTag(constructedEntity, "armor"),
            OpponentHealth = (((object)constructedEntity2 != null && constructedEntity2.Health > 0) ? constructedEntity2.Health : 30),
            OpponentArmor = ReadIntTag(constructedEntity2, "armor"),
            Hand = array,
            FriendlyBoard = array2,
            EnemyBoard = array3,
            FriendlyWeapon = friendlyWeapon,
            EnemyWeapon = enemyWeapon,
            LocalHeroPower = constructedEntity3,
            LocalHeroPowers = array4,
            SecretsCount = secretsCount,
            DiscoverOptions = array5,
            ChoiceOptions = array7,
            CurrentPlayableActions = readOnlyList2,
            PredictorOptions = readOnlyList.Select((UnityConstructedOption option) => new ConstructedPredictorOption { Id = option.Index, EntityId = option.MainEntityId, PlayerId = ((option.MainPlayerId > 0) ? option.MainPlayerId : (cards.FirstOrDefault((UnityCard card) => card.EntityNumber == option.MainEntityId)?.PlayerId ?? 0)), Type = PredictorOptionType(option.Type, option.TypeName), Error = option.Error, TargetEntityIds = option.MainValidTargetEntityIds, SubOptions = option.SubOptions.Select((UnityConstructedSubOption subOption) => new ConstructedPredictorSubOption { Id = subOption.Index, EntityId = subOption.EntityId, CardId = subOption.CardId, Error = subOption.Error, TargetEntityIds = subOption.ValidTargetEntityIds }).ToArray() }).ToArray(),
            Tags = tags
        };
    }

    private string ResolveMatchId(ConstructedPhase phase, int turn)
    {
        // Entity IDs/player IDs and gameTurn are reused. A turn counter is not
        // a match identifier; retain an ID until a real lifecycle boundary.
        if (phase is ConstructedPhase.Queue or ConstructedPhase.GameOver)
        {
            _nextMatchPending = true;
            return _sessionMatchId;
        }
        if (phase == ConstructedPhase.Unknown) return _sessionMatchId;
        if (_sessionMatchId.Length == 0 || _nextMatchPending ||
            (phase == ConstructedPhase.Mulligan && _highestMatchTurn > 2))
        {
            _sessionMatchId = "session-" + Guid.NewGuid().ToString("N");
            _nextMatchPending = false;
            _highestMatchTurn = 0;
        }
        _highestMatchTurn = Math.Max(_highestMatchTurn, turn);
        return _sessionMatchId;
    }

    private ConstructedGameState BuildUnknownState(string reason)
    {
        return new ConstructedGameState
        {
            Format = _format,
            Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "UnityBridge",
                ["reason"] = reason
            }
        };
    }

    private StateStability UpdateStability(ConstructedPhase phase, string handSignature, string boardSignature, string actionSignature, string choiceSignature)
    {
        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        if (phase != _lastPhase || _phaseStableSince == DateTimeOffset.MinValue)
        {
            _lastPhase = phase;
            _phaseStableSince = utcNow;
        }

        if (!string.Equals(handSignature, _lastHandSignature, StringComparison.Ordinal) || _handStableSince == DateTimeOffset.MinValue)
        {
            _lastHandSignature = handSignature;
            _handStableSince = utcNow;
        }

        if (!string.Equals(boardSignature, _lastBoardSignature, StringComparison.Ordinal) || _boardStableSince == DateTimeOffset.MinValue)
        {
            _lastBoardSignature = boardSignature;
            _boardStableSince = utcNow;
        }

        if (!string.Equals(actionSignature, _lastActionSignature, StringComparison.Ordinal) || _actionStableSince == DateTimeOffset.MinValue)
        {
            _lastActionSignature = actionSignature;
            _actionStableSince = utcNow;
        }

        if (!string.Equals(choiceSignature, _lastChoiceSignature, StringComparison.Ordinal) || _choiceStableSince == DateTimeOffset.MinValue)
        {
            _lastChoiceSignature = choiceSignature;
            _choiceStableSince = utcNow;
        }

        return new StateStability((int)Math.Max(0.0, (utcNow - _phaseStableSince).TotalMilliseconds), (int)Math.Max(0.0, (utcNow - _handStableSince).TotalMilliseconds), (int)Math.Max(0.0, (utcNow - _boardStableSince).TotalMilliseconds), (int)Math.Max(0.0, (utcNow - _actionStableSince).TotalMilliseconds), (int)Math.Max(0.0, (utcNow - _choiceStableSince).TotalMilliseconds));
    }

    private static string BuildEntityListSignature(IEnumerable<ConstructedEntity> entities)
    {
        return string.Join("|", entities.Select((ConstructedEntity entity) => string.Join(":", entity.EntityId, entity.CardId, entity.Zone, entity.Cost, entity.Attack, entity.Health, entity.Damage, entity.CanAttack, entity.HasTaunt, entity.HasDivineShield, entity.HasRush, entity.HasCharge)));
    }

    private static string BuildActionListSignature(IEnumerable<ConstructedAction> actions)
    {
        return string.Join("|", actions.Select((ConstructedAction action) => string.Join(":", action.Type, action.SourceEntityId, action.TargetEntityId, action.OptionIndex)));
    }

    private static string BuildBoardSignature(ConstructedEntity? localHero, ConstructedEntity? opponentHero, IReadOnlyList<ConstructedEntity> friendlyBoard, IReadOnlyList<ConstructedEntity> enemyBoard, ConstructedEntity? friendlyWeapon, ConstructedEntity? enemyWeapon, IReadOnlyList<ConstructedEntity> localHeroPowers)
    {
        return string.Join("||", BuildEntitySignature(localHero), BuildEntitySignature(opponentHero), BuildEntityListSignature(friendlyBoard), BuildEntityListSignature(enemyBoard), BuildEntitySignature(friendlyWeapon), BuildEntitySignature(enemyWeapon), BuildEntityListSignature(localHeroPowers));
    }

    private static string BuildEntitySignature(ConstructedEntity? entity)
    {
        if ((object)entity != null)
        {
            return string.Join(":", entity.EntityId, entity.CardId, entity.Zone, entity.Cost, entity.Attack, entity.Health, entity.Damage, entity.CanAttack, entity.HasTaunt, entity.HasDivineShield);
        }

        return "";
    }

    private ConstructedPhase DetectPhase(JsonElement data, IReadOnlyList<UnityCard> cards, int choiceCardCount, int handCount, int friendlyBoardCount, int enemyBoardCount)
    {
        if (GetBool(data, "hasBattlegroundsEndGameScreen") || HasReliableConstructedEndGameScreen(data))
        {
            return ConstructedPhase.GameOver;
        }

        if (GetBool(data, "hasMatchingPopup"))
        {
            return ConstructedPhase.Queue;
        }

        if (handCount > 0 && (GetBool(data, "hasMulliganConfirmButton") || GetBool(data, "isMulliganWaitingForUserInput")))
        {
            return ConstructedPhase.Mulligan;
        }

        if (HasActiveChoicePrompt(data, choiceCardCount, handCount, friendlyBoardCount, enemyBoardCount))
        {
            return ConstructedPhase.Choice;
        }

        if (cards.Count <= 0 || (handCount <= 0 && friendlyBoardCount <= 0 && enemyBoardCount <= 0 && !HasAnyHero(cards)))
        {
            return ConstructedPhase.Unknown;
        }

        if (TryReadInternalFriendlyTurn(data, out var friendlyTurn))
        {
            if (!friendlyTurn)
            {
                return ConstructedPhase.OpponentTurn;
            }

            return ConstructedPhase.LocalTurn;
        }

        if (HasEndTurnOption(data) || HasLocalPlayableResponse(cards))
        {
            return ConstructedPhase.LocalTurn;
        }

        if (TryReadTurnTimerFriendly(data, out var friendlyTurn2))
        {
            if (!friendlyTurn2)
            {
                return ConstructedPhase.OpponentTurn;
            }

            return ConstructedPhase.LocalTurn;
        }

        if (handCount > 0 && cards.Any((UnityCard card) => card.PlayerId == DetectLocalPlayerId(cards) && IsAttackOption(card)))
        {
            return ConstructedPhase.LocalTurn;
        }

        return ConstructedPhase.Unknown;
    }

    private bool HasActiveChoicePrompt(JsonElement data, int choiceCardCount, int handCount, int friendlyBoardCount, int enemyBoardCount)
    {
        if (choiceCardCount > 0)
        {
            ClearEmptyChoicePromptTracking();
            return true;
        }

        if (!GetBool(data, "hasStandardChoicePrompt"))
        {
            ClearEmptyChoicePromptTracking();
            return false;
        }

        DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        string text = BuildEmptyChoicePromptKey(data, handCount, friendlyBoardCount, enemyBoardCount);
        if (!string.Equals(text, _emptyChoicePromptKey, StringComparison.Ordinal))
        {
            _emptyChoicePromptKey = text;
            _emptyChoicePromptSince = utcNow;
            _emptyChoicePromptAgeMs = 0;
            _emptyChoicePromptIgnored = false;
            return true;
        }

        _emptyChoicePromptAgeMs = (int)Math.Max(0.0, (utcNow - _emptyChoicePromptSince).TotalMilliseconds);
        _emptyChoicePromptIgnored = _emptyChoicePromptAgeMs > 3500;
        return !_emptyChoicePromptIgnored;
    }

    private static string BuildEmptyChoicePromptKey(JsonElement data, int handCount, int friendlyBoardCount, int enemyBoardCount)
    {
        return string.Join(":", GetInt(data, "gameTurn"), ReadNestedInt(data, "mana", "ready", 0), ReadNestedInt(data, "mana", "total", 0), ReadNestedInt(data, "turnOwner", "currentPlayerId", 0), handCount, friendlyBoardCount, enemyBoardCount, GetBool(data, "friendlyChoicesPending"), GetBool(data, "hasChoicePrompt"));
    }

    private void ClearEmptyChoicePromptTracking()
    {
        _emptyChoicePromptKey = "";
        _emptyChoicePromptSince = DateTimeOffset.MinValue;
        _emptyChoicePromptAgeMs = 0;
        _emptyChoicePromptIgnored = false;
    }

    private static bool TryReadInternalFriendlyTurn(JsonElement data, out bool friendlyTurn)
    {
        friendlyTurn = false;
        JsonElement value2 = default;
        bool flag = data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("turnOwner", out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("isFriendlySidePlayerTurn", out value2);
        if (!flag)
        {
            JsonValueKind valueKind = value2.ValueKind;
            bool flag2 = valueKind - 5 <= JsonValueKind.Object;
            flag = !flag2;
        }

        if (flag)
        {
            return false;
        }

        friendlyTurn = value2.ValueKind == JsonValueKind.True;
        return true;
    }

    private static IReadOnlyList<ConstructedAction> BuildCurrentPlayableActions(IReadOnlyList<ConstructedEntity> hand, IReadOnlyList<ConstructedEntity> friendlyBoard, ConstructedEntity? localHero, IReadOnlyList<ConstructedEntity> localHeroPowers, ConstructedEntity? opponentHero, IReadOnlyList<ConstructedEntity> enemyBoard, int manaAvailable, ConstructedPhase phase, IReadOnlyList<UnityConstructedOption> constructedOptions)
    {
        if (phase != ConstructedPhase.LocalTurn)
        {
            return Array.Empty<ConstructedAction>();
        }

        if (constructedOptions.Count > 0)
        {
            return BuildActionsFromInternalOptions(hand, friendlyBoard, localHero, localHeroPowers, opponentHero, enemyBoard, manaAvailable, constructedOptions);
        }

        List<ConstructedAction> list = new List<ConstructedAction>();
        ConstructedGameState targetRuleState = new ConstructedGameState
        {
            EnemyBoard = enemyBoard
        };
        ConstructedEntity[] source = enemyBoard.Cast<ConstructedEntity>().Concat(((object)opponentHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { opponentHero }).Concat(friendlyBoard).Concat(((object)localHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { localHero }).ToArray();
        foreach (ConstructedEntity card in hand.Where((ConstructedEntity constructedEntity) => constructedEntity.Cost <= manaAvailable && IsPlayable(constructedEntity)))
        {
            if (NeedsTarget(card))
            {
                foreach (ConstructedEntity item in source.Where((ConstructedEntity target) => ConstructedActionRules.IsTargetAllowed(targetRuleState, ConstructedActionType.PlayCardWithTarget, card, target, out string _)))
                {
                    list.Add(new ConstructedAction { Type = ConstructedActionType.PlayCardWithTarget, SourceEntityId = card.EntityId, TargetEntityId = item.EntityId, Parameters = BuildTargetedParameters(card, item), Reason = "UnityBridge 读到当前手牌可响应" });
                }
            }
            else
            {
                list.Add(new ConstructedAction { Type = ConstructedActionType.PlayCard, SourceEntityId = card.EntityId, Parameters = BuildEntityParameters(card), Reason = "UnityBridge 读到当前手牌可响应" });
            }
        }

        ConstructedEntity[] array = ConstructedActionRules.TargetableEnemyBoard(targetRuleState).Concat(((object)opponentHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { opponentHero }).ToArray();
        ConstructedEntity[] array2 = ConstructedActionRules.TargetableEnemyTaunts(targetRuleState).ToArray();
        ConstructedEntity[] source2 = ((array2.Length != 0) ? array2 : array);
        foreach (ConstructedEntity attacker in friendlyBoard.Where((ConstructedEntity constructedEntity) => constructedEntity.CanAttack))
        {
            foreach (ConstructedEntity item2 in source2.Where((ConstructedEntity target) => ConstructedActionRules.IsTargetAllowed(targetRuleState, ConstructedActionType.Attack, attacker, target, out string _)))
            {
                list.Add(new ConstructedAction { Type = ConstructedActionType.Attack, SourceEntityId = attacker.EntityId, TargetEntityId = item2.EntityId, Parameters = BuildTargetedParameters(attacker, item2), Reason = "UnityBridge 读到当前随从可响应" });
            }
        }

        if ((object)localHero != null && localHero.CanAttack)
        {
            foreach (ConstructedEntity item3 in source2.Where((ConstructedEntity target) => ConstructedActionRules.IsTargetAllowed(targetRuleState, ConstructedActionType.Attack, localHero, target, out string _)))
            {
                list.Add(new ConstructedAction { Type = ConstructedActionType.Attack, SourceEntityId = localHero.EntityId, TargetEntityId = item3.EntityId, Parameters = BuildTargetedParameters(localHero, item3), Reason = "UnityBridge 读到当前英雄可攻击" });
            }
        }

        foreach (ConstructedEntity localHeroPower in localHeroPowers.Where((ConstructedEntity power) => IsPlayable(power) && (power.Cost <= manaAvailable || HasExplicitPlayableResponse(power))))
        {
            if (NeedsTarget(localHeroPower))
            {
                foreach (ConstructedEntity item4 in source.Where((ConstructedEntity target) => ConstructedActionRules.IsTargetAllowed(targetRuleState, ConstructedActionType.UseHeroPowerWithTarget, localHeroPower, target, out string _)))
                {
                    list.Add(new ConstructedAction { Type = ConstructedActionType.UseHeroPowerWithTarget, SourceEntityId = localHeroPower.EntityId, TargetEntityId = item4.EntityId, Parameters = BuildTargetedParameters(localHeroPower, item4), Reason = "UnityBridge 读到当前英雄技能可响应" });
                }
            }
            else
            {
                list.Add(new ConstructedAction { Type = ConstructedActionType.UseHeroPower, SourceEntityId = localHeroPower.EntityId, Parameters = BuildEntityParameters(localHeroPower), Reason = "UnityBridge 读到当前英雄技能可响应" });
            }
        }

        return list;
    }

    private static IReadOnlyList<ConstructedAction> BuildActionsFromInternalOptions(IReadOnlyList<ConstructedEntity> hand, IReadOnlyList<ConstructedEntity> friendlyBoard, ConstructedEntity? localHero, IReadOnlyList<ConstructedEntity> localHeroPowers, ConstructedEntity? opponentHero, IReadOnlyList<ConstructedEntity> enemyBoard, int manaAvailable, IReadOnlyList<UnityConstructedOption> options)
    {
        Dictionary<string, ConstructedEntity> dictionary = hand.Concat(friendlyBoard).Concat(((object)localHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { localHero }).Concat(localHeroPowers).GroupBy((ConstructedEntity entity) => entity.EntityId, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, ConstructedEntity> group) => group.Key, (IGrouping<string, ConstructedEntity> group) => group.First(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ConstructedEntity> dictionary2 = hand.Concat(enemyBoard).Concat(((object)opponentHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { opponentHero }).Concat(friendlyBoard).Concat(((object)localHero == null) ? Array.Empty<ConstructedEntity>() : new ConstructedEntity[1] { localHero }).GroupBy((ConstructedEntity entity) => entity.EntityId, StringComparer.OrdinalIgnoreCase).ToDictionary((IGrouping<string, ConstructedEntity> group) => group.Key, (IGrouping<string, ConstructedEntity> group) => group.First(), StringComparer.OrdinalIgnoreCase);
        List<ConstructedAction> list = new List<ConstructedAction>();
        foreach (UnityConstructedOption option in options.Where((UnityConstructedOption unityConstructedOption) => unityConstructedOption.MainEntityId > 0))
        {
            string key = option.MainEntityId.ToString(CultureInfo.InvariantCulture);
            if (!dictionary.TryGetValue(key, out var value))
            {
                continue;
            }

            bool siblingHasTargets = options.Any((UnityConstructedOption candidate) => candidate.MainEntityId == option.MainEntityId && candidate.HasTargets);
            UnityConstructedOption[] array = options.Where((UnityConstructedOption candidate) => candidate.MainEntityId == option.MainEntityId && !candidate.HasTargets).ToArray().Where(IsTradeInferenceCandidate).ToArray();
            bool flag = IsTradeInferenceCandidate(option) && IsInferredTradeOption(value, manaAvailable, option.HasTargets, siblingHasTargets, array.Length, array.Length != 0 && option.Index == array.Max((UnityConstructedOption candidate) => candidate.Index));
            bool flag2 = value.Zone == ConstructedZone.Hand && IsPlayable(value) && (option.Error.Equals("INVALID", StringComparison.OrdinalIgnoreCase) || option.Error.Equals("NONE", StringComparison.OrdinalIgnoreCase));
            if (!option.MainIsValid && option.ValidTargetEntityIds.Count == 0 && !option.IsTrade && !option.TypeName.Contains("TRADE", StringComparison.OrdinalIgnoreCase) && !flag2 && !flag)
            {
                continue;
            }

            bool flag3 = option.HasTargets && option.ValidTargetEntityIds.Count > 0;
            bool flag4 = option.MainIsValid && !option.HasTargets;
            if ((value.Zone == ConstructedZone.Hand || value.IsHeroPower) && !IsPlayable(value) && !flag3 && !flag4)
            {
                continue;
            }

            ConstructedActionType constructedActionType;
            if (((option.IsTrade || option.TypeName.Contains("TRADE", StringComparison.OrdinalIgnoreCase)) | flag) && value.Zone == ConstructedZone.Hand && value.IsTradeable)
            {
                constructedActionType = ConstructedActionType.TradeCard;
            }
            else if (value.IsHeroPower)
            {
                constructedActionType = (option.HasTargets ? ConstructedActionType.UseHeroPowerWithTarget : ConstructedActionType.UseHeroPower);
            }
            else if (value.Zone != ConstructedZone.Hand)
            {
                if (value.IsLocation)
                {
                    constructedActionType = (option.HasTargets ? ConstructedActionType.UseLocationWithTarget : ConstructedActionType.UseLocation);
                }
                else
                {
                    constructedActionType = ConstructedActionType.Attack;
                }
            }
            else
            {
                constructedActionType = (option.HasTargets ? ConstructedActionType.PlayCardWithTarget : ConstructedActionType.PlayCard);
            }

            bool flag5 = option.SubOptions.Count > 0 || !string.IsNullOrWhiteSpace(option.PowerKeyword) || (constructedActionType == ConstructedActionType.Attack && !option.HasTargets);
            if (flag5 && !option.HasTargets)
            {
                list.Add(new ConstructedAction { Type = ConstructedActionType.InternalOption, SourceEntityId = value.EntityId, OptionIndex = option.Index, Parameters = BuildInternalOptionParameters(BuildEntityParameters(value), option), Reason = "客户端内部 OptionsPacket 确认该特殊动作合法" });
            }
            else if (flag5)
            {
                foreach (int validTargetEntityId in option.ValidTargetEntityIds)
                {
                    string key2 = validTargetEntityId.ToString(CultureInfo.InvariantCulture);
                    if (dictionary2.TryGetValue(key2, out var value2))
                    {
                        list.Add(new ConstructedAction { Type = ConstructedActionType.InternalOption, SourceEntityId = value.EntityId, TargetEntityId = value2.EntityId, OptionIndex = option.Index, Parameters = BuildInternalOptionParameters(BuildTargetedParameters(value, value2), option), Reason = "客户端内部 OptionsPacket 确认该特殊源和目标合法" });
                    }
                }
            }

            if (!option.HasTargets)
            {
                if (constructedActionType != ConstructedActionType.Attack)
                {
                    list.Add(new ConstructedAction { Type = constructedActionType, SourceEntityId = value.EntityId, OptionIndex = option.Index, Parameters = BuildInternalOptionParameters(BuildEntityParameters(value), option), Reason = "客户端内部 OptionsPacket 确认该动作合法" });
                }

                continue;
            }

            foreach (int validTargetEntityId2 in option.ValidTargetEntityIds)
            {
                string key3 = validTargetEntityId2.ToString(CultureInfo.InvariantCulture);
                if (dictionary2.TryGetValue(key3, out var value3))
                {
                    list.Add(new ConstructedAction { Type = constructedActionType, SourceEntityId = value.EntityId, TargetEntityId = value3.EntityId, OptionIndex = option.Index, Parameters = BuildInternalOptionParameters(BuildTargetedParameters(value, value3), option), Reason = "客户端内部 OptionsPacket 确认源和目标合法" });
                }
            }
        }

        return (
            from @group in list.GroupBy((ConstructedAction action) => $"{action.Type}|{action.SourceEntityId}|{action.TargetEntityId}|" + ((action.Type == ConstructedActionType.InternalOption) ? action.OptionIndex : new int? (-1)), StringComparer.OrdinalIgnoreCase)select @group.First()).ToArray();
    }

    internal static bool IsInferredTradeOption(ConstructedEntity source, int manaAvailable, bool optionHasTargets, bool siblingHasTargets, int untargetedSiblingCount, bool isLastUntargetedSibling)
    {
        if ((!source.IsTradeable || source.Zone != ConstructedZone.Hand || manaAvailable < 1) | optionHasTargets)
        {
            return false;
        }

        if (untargetedSiblingCount > 1)
        {
            return isLastUntargetedSibling;
        }

        if (!siblingHasTargets && source.Cost <= manaAvailable)
        {
            return !IsPlayable(source);
        }

        return true;
    }

    private static bool IsTradeInferenceCandidate(UnityConstructedOption option)
    {
        return IsTradeInferenceCandidate(option.MainIsValid, option.Error, option.IsTrade, option.TypeName);
    }

    internal static bool IsTradeInferenceCandidate(bool mainIsValid, string optionError, bool isTrade, string typeName)
    {
        if (!(mainIsValid | isTrade) && !typeName.Contains("TRADE", StringComparison.OrdinalIgnoreCase) && !string.Equals(optionError, "INVALID", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(optionError, "NONE", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static ConstructedEntity[] MarkInternallyConfirmedPlayableEntities(IReadOnlyList<ConstructedEntity> entities, IReadOnlyList<ConstructedAction> playableActions)
    {
        return entities.Select((ConstructedEntity entity) => MarkInternallyConfirmedPlayableEntity(entity, playableActions)).ToArray();
    }

    private static ConstructedEntity MarkInternallyConfirmedPlayableEntity(ConstructedEntity entity, IReadOnlyList<ConstructedAction> playableActions)
    {
        if (!playableActions.Any((ConstructedAction action) =>
        {
            bool result = default;
            return (string.Equals(action.SourceEntityId, entity.EntityId, StringComparison.OrdinalIgnoreCase) && action.Parameters.TryGetValue("unity.internalOption", out string value2) && bool.TryParse(value2, out result)) & result;
        }))
        {
            return entity;
        }

        Dictionary<string, string> dictionary = new Dictionary<string, string>(entity.Tags, StringComparer.OrdinalIgnoreCase);
        if (dictionary.TryGetValue("unity.hasResponse", out var value))
        {
            dictionary["unity.reportedHasResponse"] = value;
        }

        dictionary["unity.hasResponse"] = bool.TrueString;
        dictionary["unity.internalOptionConfirmed"] = bool.TrueString;
        return entity with
        {
            Tags = dictionary
        };
    }

    private static IReadOnlyDictionary<string, string> BuildInternalOptionParameters(IReadOnlyDictionary<string, string> parameters, UnityConstructedOption option)
    {
        Dictionary<string, string> dictionary = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["unity.internalOption"] = bool.TrueString
        };
        dictionary["unity.optionIndex"] = option.Index.ToString(CultureInfo.InvariantCulture);
        dictionary["unity.optionType"] = option.Type.ToString(CultureInfo.InvariantCulture);
        dictionary["unity.optionTypeName"] = option.TypeName;
        dictionary["unity.isTrade"] = option.IsTrade.ToString();
        dictionary["unity.optionPowerKeyword"] = option.PowerKeyword;
        dictionary["unity.subOptionCount"] = option.SubOptions.Count.ToString(CultureInfo.InvariantCulture);
        foreach (UnityConstructedSubOption subOption in option.SubOptions)
        {
            string text = $"unity.subOption.{subOption.Index}";
            dictionary[text + ".entityId"] = subOption.EntityId.ToString(CultureInfo.InvariantCulture);
            dictionary[text + ".cardId"] = subOption.CardId;
            dictionary[text + ".zonePosition"] = subOption.ZonePosition.ToString(CultureInfo.InvariantCulture);
            dictionary[text + ".powerKeyword"] = subOption.PowerKeyword;
            dictionary[text + ".targetCount"] = subOption.ValidTargetEntityIds.Count.ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < subOption.ValidTargetEntityIds.Count; i++)
            {
                dictionary[$"{text}.target.{i}"] = subOption.ValidTargetEntityIds[i].ToString(CultureInfo.InvariantCulture);
            }
        }

        return dictionary;
    }

    private static bool NeedsTarget(ConstructedEntity card)
    {
        string text = $"{card.Name} {card.CardId} {ReadTag(card, "unity.cardText")}";
        if (card.IsHeroPower)
        {
            return IsTargetedHeroPower(text);
        }

        if (!card.IsSpell || IsRandomOrAreaEffectText(text))
        {
            return false;
        }

        if (!ContainsAnyText(text, "目标", "target", "一个角色", "一个随从", "一个敌人", "任意角色", "a character", "a minion", "an enemy"))
        {
            return ContainsDamageText(text);
        }

        return true;
    }

    private static bool IsTargetedHeroPower(string text)
    {
        if (!ContainsAnyText(text, "MAGE", "HERO_08", "法师", "火焰冲击", "Fireblast"))
        {
            return ContainsAnyText(text, "PRIEST", "HERO_09", "牧师", "次级治疗术", "治疗", "Lesser Heal");
        }

        return true;
    }

    private static bool IsRandomOrAreaEffectText(string text)
    {
        return ContainsAnyText(text, "随机", "random", "所有", "全体", "全部", "每个", "all ", "each ");
    }

    private static bool ContainsDamageText(string text)
    {
        return ContainsAnyText(text, "造成", "伤害", "damage", "deal");
    }

    private static bool ContainsAnyText(string text, params string[] needles)
    {
        return needles.Any((string needle) => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPlayable(ConstructedEntity card)
    {
        string value;
        bool result = default;
        return (!card.Tags.TryGetValue("unity.hasResponse", out value) || !bool.TryParse(value, out result)) | result;
    }

    private static bool HasExplicitPlayableResponse(ConstructedEntity entity)
    {
        string value;
        bool result = default;
        return (entity.Tags.TryGetValue("unity.hasResponse", out value) && bool.TryParse(value, out result)) & result;
    }

    private static List<UnityCard> ReadCardsFromProperty(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return new List<UnityCard>();
        }

        return (
            from card in value.EnumerateArray().Select(TryParseCard)
            where (object)card != null
            select (card)into card
                group card by card.EntityId into @group
                    select @group.OrderBy((UnityCard card) => card.Path.Length).First()).ToList();
    }

    private static IReadOnlyList<int> ReadIntArray(JsonElement data, string property)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        return (
            from jsonElement in value.EnumerateArray()select (jsonElement.ValueKind != JsonValueKind.Number || !jsonElement.TryGetInt32(out var value2)) ? ((jsonElement.ValueKind == JsonValueKind.String && int.TryParse(jsonElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) ? result : 0) : value2 into num
                where num > 0
                select num).Distinct().Take(32).ToArray();
    }

    private static IReadOnlyList<UnityConstructedOption> ReadConstructedOptions(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("constructedOptions", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<UnityConstructedOption>();
        }

        return value.EnumerateArray().Select((JsonElement option) =>
        {
            int[] array = ((option.TryGetProperty("targets", out var value2) && value2.ValueKind == JsonValueKind.Array) ? (
                from target in value2.EnumerateArray()
                where GetBool(target, "valid")select GetInt(target, "entityId")into entityId
                    where entityId > 0
                    select entityId).Distinct().ToArray() : Array.Empty<int>());
            UnityConstructedSubOption[] array2 = ((option.TryGetProperty("subOptions", out var value3) && value3.ValueKind == JsonValueKind.Array) ? (
                from subOption in value3.EnumerateArray()select new UnityConstructedSubOption(GetInt(subOption, "index"), GetInt(subOption, "entityId"), GetString(subOption, "cardId") ?? "", GetInt(subOption, "zonePosition"), GetString(subOption, "powerKeyword") ?? "", ReadConstructedOptionError(subOption), (subOption.TryGetProperty("targets", out var value5) && value5.ValueKind == JsonValueKind.Array) ? (
                    from target in value5.EnumerateArray()
                    where GetBool(target, "valid")select GetInt(target, "entityId")into entityId
                        where entityId > 0
                        select entityId).Distinct().ToArray() : Array.Empty<int>())).ToArray() : Array.Empty<UnityConstructedSubOption>());
            int[] validTargetEntityIds = array.Concat(array2.SelectMany((UnityConstructedSubOption subOption) => subOption.ValidTargetEntityIds)).Distinct().ToArray();
            bool hasTargets = (option.TryGetProperty("targets", out var value4) && value4.ValueKind == JsonValueKind.Array && value4.GetArrayLength() > 0) || array2.Any((UnityConstructedSubOption subOption) => subOption.ValidTargetEntityIds.Count > 0);
            bool mainIsValid = ReadConstructedOptionMainValidity(option);
            return new UnityConstructedOption(GetInt(option, "index"), GetInt(option, "type"), GetString(option, "typeName") ?? "", GetInt(option, "mainEntityId"), GetInt(option, "mainPlayerId"), GetBool(option, "isTrade"), GetString(option, "powerKeyword") ?? "", ReadConstructedOptionError(option), mainIsValid, hasTargets, array, validTargetEntityIds, array2);
        }).ToArray();
    }

    private static string ReadConstructedOptionError(JsonElement option)
    {
        if (option.TryGetProperty("playError", out var value) && value.ValueKind == JsonValueKind.Object)
        {
            string text = GetString(value, "playError");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return "INVALID";
    }

    private static string PredictorOptionType(int type, string typeName)
    {
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            return typeName.ToUpperInvariant();
        }

        return type switch
        {
            2 => "END_TURN",
            3 => "POWER",
            _ => type.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static bool ReadConstructedOptionMainValidity(JsonElement option)
    {
        if (!option.TryGetProperty("playError", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if ((GetString(value, "playError") ?? "").Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.TryGetProperty("valid", out var value2))
        {
            return value2.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static UnityCard? TryParseCard(JsonElement item)
    {
        string text = GetString(item, "name") ?? "";
        string text2 = GetEntityString(item, "id") ?? "";
        string text3 = GetEntityString(item, "cardId") ?? "";
        string unityZone = GetEntityString(item, "zone") ?? "";
        int result = GetEntityInt(item, "zonePosition");
        int result2 = GetEntityInt(item, "playerId");
        Match match = CardObjectNamePattern.Match(text);
        if (match.Success)
        {
            text2 = match.Groups["id"].Value;
            text3 = match.Groups["cardId"].Value;
            unityZone = match.Groups["zone"].Value;
            int.TryParse(match.Groups["zonePos"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
            int.TryParse(match.Groups["player"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result2);
            text = match.Groups["name"].Value.Trim();
        }

        if (string.IsNullOrWhiteSpace(text2) && string.IsNullOrWhiteSpace(text3))
        {
            return null;
        }

        int entityNumber = (int.TryParse(text2, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result3) ? result3 : 0);
        return new UnityCard(string.IsNullOrWhiteSpace(GetEntityString(item, "name")) ? text : GetEntityString(item, "name"), text2, entityNumber, text3, unityZone, result, result2, GetString(item, "path") ?? "", GetInt(item, "instanceId"), GetEntityBool(item, "isSpell"), GetEntityBool(item, "isWeapon"), GetEntityBool(item, "isHeroPower"), GetEntityBool(item, "isMinion"), GetEntityBool(item, "isHero") || IsHeroCardId(text3), GetOptionalEntityBool(item, "tradeable"), GetOptionalEntityBool(item, "hasResponse"), GetOptionalEntityBool(item, "isValidOption"), GetOptionalEntityBool(item, "taunt"), GetOptionalEntityBool(item, "divineShield"), GetOptionalEntityBool(item, "rush"), GetOptionalEntityBool(item, "charge"), GetOptionalEntityBool(item, "stealth"), GetOptionalEntityBool(item, "cantBeTargetedBySpells"), GetOptionalEntityBool(item, "cantBeTargetedByHeroPowers"), GetOptionalEntityBool(item, "cantBeTargetedByAbilities"), GetOptionalEntityBool(item, "cantBeTargetedByOpponentAbilities"), GetOptionalEntityBool(item, "cantBeTargetedByOpponents"), GetOptionalEntityBool(item, "immune"), GetEntityString(item, "cardText"), GetEntityString(item, "playError"), GetEntityInt(item, "attack"), GetEntityInt(item, "health"), GetEntityInt(item, "damage"), GetEntityInt(item, "armor"), Math.Max(0, GetEntityInt(item, "durability")), Math.Max(0, GetEntityInt(item, "locationCooldown")), Math.Clamp(GetEntityInt(item, "realTimeCost"), 0, 99));
    }

    private ConstructedEntity ToEntity(UnityCard card, ConstructedZone zone, bool? canAttackOverride = null)
    {
        Dictionary<string, string> tags = BuildEntityTags(card);
        HearthstoneCardMetadata hearthstoneCardMetadata = (_cardDatabase.TryGetByCardId(card.CardId, out HearthstoneCardMetadata metadata) ? metadata : null);
        if ((object)hearthstoneCardMetadata != null)
        {
            AddCardMetadataTags(tags, hearthstoneCardMetadata);
        }

        bool canAttack = canAttackOverride ?? IsAttackOption(card);
        return new ConstructedEntity
        {
            EntityId = card.EntityId,
            CardId = card.CardId,
            DbfId = (hearthstoneCardMetadata?.DbfId ?? 0),
            Name = (((object)hearthstoneCardMetadata != null && !string.IsNullOrWhiteSpace(hearthstoneCardMetadata.Name)) ? hearthstoneCardMetadata.Name : card.Name),
            RulesText = (hearthstoneCardMetadata?.PlainText ?? card.CardText ?? ""),
            CardType = (hearthstoneCardMetadata?.Type ?? ""),
            CardClass = (hearthstoneCardMetadata?.CardClass ?? ""),
            CardSet = (hearthstoneCardMetadata?.Set ?? ""),
            Rarity = (hearthstoneCardMetadata?.Rarity ?? ""),
            SpellSchool = (hearthstoneCardMetadata?.SpellSchool ?? ""),
            BaseCost = (hearthstoneCardMetadata?.Cost ?? 0),
            BaseAttack = (hearthstoneCardMetadata?.Attack ?? 0),
            BaseHealth = (hearthstoneCardMetadata?.Health ?? 0),
            BaseDurability = (hearthstoneCardMetadata?.Durability ?? 0),
            SpellDamage = (hearthstoneCardMetadata?.SpellDamage ?? 0),
            Mechanics = (hearthstoneCardMetadata?.Mechanics ?? Array.Empty<string>()),
            Races = (hearthstoneCardMetadata?.Races ?? Array.Empty<string>()),
            ReferencedTags = (hearthstoneCardMetadata?.ReferencedTags ?? Array.Empty<string>()),
            Zone = zone,
            Cost = card.Cost,
            Attack = Math.Max(0, card.Attack),
            Health = Math.Max(0, card.Health - Math.Max(0, card.Damage)),
            Damage = Math.Max(0, card.Damage),
            CanAttack = canAttack,
            HasTaunt = ResolveLiveKeyword(card.Taunt, hearthstoneCardMetadata, "TAUNT"),
            HasDivineShield = ResolveLiveKeyword(card.DivineShield, hearthstoneCardMetadata, "DIVINE_SHIELD"),
            HasRush = ResolveLiveKeyword(card.Rush, hearthstoneCardMetadata, "RUSH"),
            HasCharge = ResolveLiveKeyword(card.Charge, hearthstoneCardMetadata, "CHARGE"),
            HasStealth = ResolveLiveKeyword(card.Stealth, hearthstoneCardMetadata, "STEALTH"),
            CantBeTargetedBySpellsOrHeroPowers = ResolveLiveTargetingRestriction(card, hearthstoneCardMetadata),
            HasImmune = ResolveLiveKeyword(card.Immune, hearthstoneCardMetadata, "IMMUNE"),
            IsSpell = (card.IsSpell || IsCardType(hearthstoneCardMetadata, "SPELL")),
            IsMinion = (card.IsMinion || IsCardType(hearthstoneCardMetadata, "MINION")),
            IsWeapon = (card.IsWeapon || IsWeapon(card) || IsCardType(hearthstoneCardMetadata, "WEAPON")),
            IsHeroPower = (card.IsHeroPower || IsHeroPower(card) || IsCardType(hearthstoneCardMetadata, "HERO_POWER")),
            IsLocation = IsCardType(hearthstoneCardMetadata, "LOCATION"),
            IsTradeable = ((_useNativeTradeTags && card.Tradeable.HasValue) ? card.Tradeable.Value : (hearthstoneCardMetadata?.Mechanics.Any((string mechanic) => string.Equals(mechanic, "TRADEABLE", StringComparison.OrdinalIgnoreCase)) ?? false)),
            LocationDurability = card.Durability,
            LocationCooldown = card.LocationCooldown,
            Tags = tags
        };
    }

    private static bool IsCardType(HearthstoneCardMetadata? metadata, string type)
    {
        if ((object)metadata != null)
        {
            return string.Equals(metadata.Type, type, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool ResolveLiveKeyword(bool? liveValue, HearthstoneCardMetadata? metadata, params string[] mechanics)
    {
        return liveValue ?? HasMechanic(metadata, mechanics);
    }

    private static bool ResolveLiveTargetingRestriction(UnityCard card, HearthstoneCardMetadata? metadata)
    {
        bool? [] source = new bool? [5]
        {
            card.CantBeTargetedBySpells,
            card.CantBeTargetedByHeroPowers,
            card.CantBeTargetedByAbilities,
            card.CantBeTargetedByOpponentAbilities,
            card.CantBeTargetedByOpponents
        };
        if (!source.Any((bool? value) => value == true))
        {
            if (source.All((bool? value) => !value.HasValue))
            {
                return HasMechanic(metadata, "ELUSIVE", "CANT_BE_TARGETED_BY_SPELLS", "CANT_BE_TARGETED_BY_HERO_POWERS");
            }

            return false;
        }

        return true;
    }

    private static void AddCardMetadataTags(IDictionary<string, string> tags, HearthstoneCardMetadata metadata)
    {
        tags["hdt.matched"] = "true";
        tags["hdt.id"] = metadata.Id;
        tags["hdt.dbfId"] = metadata.DbfId.ToString(CultureInfo.InvariantCulture);
        tags["hdt.name"] = metadata.Name;
        tags["hdt.text"] = metadata.PlainText;
        tags["hdt.type"] = metadata.Type;
        tags["hdt.cardClass"] = metadata.CardClass;
        tags["hdt.set"] = metadata.Set;
        tags["hdt.rarity"] = metadata.Rarity;
        tags["hdt.spellSchool"] = metadata.SpellSchool;
        tags["hdt.baseCost"] = metadata.Cost.ToString(CultureInfo.InvariantCulture);
        tags["hdt.baseAttack"] = metadata.Attack.ToString(CultureInfo.InvariantCulture);
        tags["hdt.baseHealth"] = metadata.Health.ToString(CultureInfo.InvariantCulture);
        tags["hdt.baseDurability"] = metadata.Durability.ToString(CultureInfo.InvariantCulture);
        tags["hdt.spellDamage"] = metadata.SpellDamage.ToString(CultureInfo.InvariantCulture);
        tags["hdt.collectible"] = metadata.Collectible.ToString();
        tags["hdt.mechanics"] = string.Join(",", metadata.Mechanics);
        tags["hdt.races"] = string.Join(",", metadata.Races);
        tags["hdt.referencedTags"] = string.Join(",", metadata.ReferencedTags);
        if (!tags.ContainsKey("unity.cardText") && !string.IsNullOrWhiteSpace(metadata.PlainText))
        {
            tags["unity.cardText"] = metadata.PlainText;
        }
    }

    private static Dictionary<string, string> BuildEntityTags(UnityCard card)
    {
        Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "UnityBridge",
            ["unity.path"] = card.Path,
            ["unity.cardPath"] = card.Path,
            ["unity.name"] = card.Name,
            ["unity.cardId"] = card.CardId,
            ["unity.entityId"] = card.EntityId,
            ["unity.instanceId"] = card.InstanceId.ToString(CultureInfo.InvariantCulture),
            ["unity.cardInstanceId"] = card.InstanceId.ToString(CultureInfo.InvariantCulture),
            ["unity.zone"] = card.UnityZone,
            ["unity.zonePos"] = card.ZonePos.ToString(CultureInfo.InvariantCulture),
            ["unity.playerId"] = card.PlayerId.ToString(CultureInfo.InvariantCulture)
        };
        if (card.HasResponse.HasValue)
        {
            dictionary["unity.hasResponse"] = card.HasResponse.Value.ToString();
        }

        if (card.IsValidOption.HasValue)
        {
            dictionary["unity.isValidOption"] = card.IsValidOption.Value.ToString();
        }

        AddOptionalBoolTag(dictionary, "taunt", card.Taunt);
        AddOptionalBoolTag(dictionary, "divineShield", card.DivineShield);
        AddOptionalBoolTag(dictionary, "rush", card.Rush);
        AddOptionalBoolTag(dictionary, "charge", card.Charge);
        AddOptionalBoolTag(dictionary, "stealth", card.Stealth);
        AddOptionalBoolTag(dictionary, "cantBeTargetedBySpells", card.CantBeTargetedBySpells);
        AddOptionalBoolTag(dictionary, "cantBeTargetedByHeroPowers", card.CantBeTargetedByHeroPowers);
        AddOptionalBoolTag(dictionary, "cantBeTargetedByAbilities", card.CantBeTargetedByAbilities);
        AddOptionalBoolTag(dictionary, "cantBeTargetedByOpponentAbilities", card.CantBeTargetedByOpponentAbilities);
        AddOptionalBoolTag(dictionary, "cantBeTargetedByOpponents", card.CantBeTargetedByOpponents);
        AddOptionalBoolTag(dictionary, "immune", card.Immune);
        if (!string.IsNullOrWhiteSpace(card.CardText))
        {
            dictionary["unity.cardText"] = card.CardText;
        }

        if (!string.IsNullOrWhiteSpace(card.PlayError))
        {
            dictionary["unity.playError"] = card.PlayError;
        }

        if (card.Damage > 0)
        {
            dictionary["damage"] = card.Damage.ToString(CultureInfo.InvariantCulture);
        }

        if (card.Armor > 0)
        {
            dictionary["armor"] = card.Armor.ToString(CultureInfo.InvariantCulture);
        }

        return dictionary;
    }

    private static void AddOptionalBoolTag(IDictionary<string, string> tags, string name, bool? value)
    {
        if (value.HasValue)
        {
            tags[name] = value.Value.ToString();
        }
    }

    private IReadOnlyDictionary<string, string> BuildStateTags(JsonElement data, int localPlayerId, int opponentPlayerId, int cardCount, int choiceCardCount, StateStability stability)
    {
        Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "UnityBridge",
            ["localPlayerId"] = localPlayerId.ToString(CultureInfo.InvariantCulture),
            ["opponentPlayerId"] = opponentPlayerId.ToString(CultureInfo.InvariantCulture),
            ["unity.cardCount"] = cardCount.ToString(CultureInfo.InvariantCulture),
            ["unity.choiceCardCount"] = choiceCardCount.ToString(CultureInfo.InvariantCulture),
            ["unity.targetChoiceCount"] = GetInt(data, "friendlyTargetChoiceCount").ToString(CultureInfo.InvariantCulture),
            ["unity.targetChoiceId"] = GetInt(data, "friendlyTargetChoiceId").ToString(CultureInfo.InvariantCulture),
            ["unity.targetChoiceCountMin"] = GetInt(data, "friendlyTargetChoiceCountMin").ToString(CultureInfo.InvariantCulture),
            ["unity.targetChoiceCountMax"] = GetInt(data, "friendlyTargetChoiceCountMax").ToString(CultureInfo.InvariantCulture),
            ["unity.friendlyTargetChoicePending"] = GetBool(data, "friendlyTargetChoicePending").ToString(),
            ["unity.hasChoicePrompt"] = GetBool(data, "hasChoicePrompt").ToString(),
            ["unity.friendlyChoicesPending"] = GetBool(data, "friendlyChoicesPending").ToString(),
            ["unity.hasStandardChoicePrompt"] = GetBool(data, "hasStandardChoicePrompt").ToString(),
            ["unity.hasMulliganConfirmButton"] = GetBool(data, "hasMulliganConfirmButton").ToString(),
            ["unity.isMulliganWaitingForUserInput"] = GetBool(data, "isMulliganWaitingForUserInput").ToString(),
            ["unity.mulliganIntroStateAvailable"] = GetBool(data, "mulliganIntroStateAvailable").ToString(),
            ["unity.mulliganIntroComplete"] = GetBool(data, "mulliganIntroComplete").ToString(),
            ["unity.mulliganIntroRunning"] = GetBool(data, "mulliganIntroRunning").ToString(),
            ["unity.mulliganCardsTransitioning"] = GetBool(data, "mulliganCardsTransitioning").ToString(),
            ["unity.hasMatchingPopup"] = GetBool(data, "hasMatchingPopup").ToString(),
            ["unity.optionsPacketId"] = GetInt(data, "constructedOptionsPacketId").ToString(CultureInfo.InvariantCulture),
            ["unity.hasConstructedEndGameScreen"] = GetBool(data, "hasConstructedEndGameScreen").ToString(),
            ["unity.hasBattlegroundsEndGameScreen"] = GetBool(data, "hasBattlegroundsEndGameScreen").ToString(),
            ["constructed.result"] = GetString(data, "constructedResult") ?? "Unknown",
            ["constructed.friendlyPlayState"] = GetInt(data, "friendlyPlayState").ToString(CultureInfo.InvariantCulture),
            ["constructed.opposingPlayState"] = GetInt(data, "opposingPlayState").ToString(CultureInfo.InvariantCulture),
            ["constructed.emptyChoicePromptGraceMs"] = 3500.ToString(CultureInfo.InvariantCulture),
            ["constructed.emptyChoicePromptAgeMs"] = _emptyChoicePromptAgeMs.ToString(CultureInfo.InvariantCulture),
            ["constructed.emptyChoicePromptIgnored"] = _emptyChoicePromptIgnored.ToString(),
            ["constructed.phaseStableMs"] = stability.PhaseStableMs.ToString(CultureInfo.InvariantCulture),
            ["constructed.handStableMs"] = stability.HandStableMs.ToString(CultureInfo.InvariantCulture),
            ["constructed.boardStableMs"] = stability.BoardStableMs.ToString(CultureInfo.InvariantCulture),
            ["constructed.actionStableMs"] = stability.ActionStableMs.ToString(CultureInfo.InvariantCulture),
            ["constructed.choiceStableMs"] = stability.ChoiceStableMs.ToString(CultureInfo.InvariantCulture)
        };
        dictionary["hdt.loaded"] = _cardDatabase.IsLoaded.ToString();
        dictionary["hdt.cardCount"] = _cardDatabase.Count.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(_cardDatabase.SourcePath))
        {
            dictionary["hdt.source"] = _cardDatabase.SourcePath;
        }

        if (!string.IsNullOrWhiteSpace(_cardDatabase.LoadError))
        {
            dictionary["hdt.error"] = _cardDatabase.LoadError;
        }

        AddLitePerformanceTags(dictionary, data);
        AddTurnStartTags(dictionary, data);
        AddTurnTimerTags(dictionary, data);
        AddEndTurnTags(dictionary, data);
        return dictionary;
    }

    private static ConstructedGameResult ParseConstructedResult(string? value)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "win":
            case "won":
            case "victory":
                return ConstructedGameResult.Win;
            case "loss":
            case "lost":
            case "defeat":
            case "conceded":
                return ConstructedGameResult.Loss;
            case "tie":
            case "draw":
            case "tied":
                return ConstructedGameResult.Draw;
            default:
                return ConstructedGameResult.Unknown;
        }
    }

    private static void AddLitePerformanceTags(IDictionary<string, string> tags, JsonElement data)
    {
        string[] array = new string[4]
        {
            "elapsedMs",
            "objectCount",
            "cardCount",
            "textScanCount"
        };
        foreach (string text in array)
        {
            DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(10, 2);
            defaultInterpolatedStringHandler.AppendLiteral("unity.lite");
            defaultInterpolatedStringHandler.AppendFormatted(char.ToUpperInvariant(text[0]));
            string text2 = text;
            defaultInterpolatedStringHandler.AppendFormatted(text2.Substring(1, text2.Length - 1));
            tags[defaultInterpolatedStringHandler.ToStringAndClear()] = GetInt(data, text).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void AddTurnTimerTags(IDictionary<string, string> tags, JsonElement data)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("turnTimer", out var value) && value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("friendlySidePlayer", out var value2))
            {
                tags["unity.turnTimerFriendlySidePlayer"] = ((value2.ValueKind == JsonValueKind.True) ? "true" : "false");
            }

            if (value.TryGetProperty("state", out var value3) && value3.ValueKind == JsonValueKind.String)
            {
                tags["unity.turnTimerState"] = value3.GetString() ?? "";
            }

            bool flag = value.TryGetProperty("waitingForTurnStartManager", out var value4);
            if (flag)
            {
                JsonValueKind valueKind = value4.ValueKind;
                bool flag2 = valueKind - 5 <= JsonValueKind.Object;
                flag = flag2;
            }

            if (flag)
            {
                tags["unity.turnTimer.waitingForTurnStartManager"] = ((value4.ValueKind == JsonValueKind.True) ? "true" : "false");
            }

            flag = value.TryGetProperty("ropeActive", out var value5);
            if (flag)
            {
                JsonValueKind valueKind = value5.ValueKind;
                bool flag2 = valueKind - 5 <= JsonValueKind.Object;
                flag = flag2;
            }

            if (flag)
            {
                tags["unity.turnTimer.ropeActive"] = ((value5.ValueKind == JsonValueKind.True) ? "true" : "false");
            }

            if (value.TryGetProperty("countdownTimeoutSec", out var value6) && value6.ValueKind == JsonValueKind.Number && value6.TryGetInt32(out var value7))
            {
                tags["unity.turnTimer.countdownTimeoutSec"] = value7.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private static void AddTurnStartTags(IDictionary<string, string> tags, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("turnStart", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string[] array = new string[3]
        {
            "manaCrystalsFilled",
            "manaCrystalsGained",
            "maxResources"
        };
        foreach (string text in array)
        {
            if (value.TryGetProperty(text, out var value2) && value2.ValueKind == JsonValueKind.Number && value2.TryGetInt32(out var value3))
            {
                tags["unity.turnStart." + text] = value3.ToString(CultureInfo.InvariantCulture);
            }
        }

        array = new string[3]
        {
            "blockingInput",
            "listeningForTurnEvents",
            "indicatorShowing"
        };
        foreach (string text2 in array)
        {
            bool flag = value.TryGetProperty(text2, out var value4);
            if (flag)
            {
                JsonValueKind valueKind = value4.ValueKind;
                bool flag2 = valueKind - 5 <= JsonValueKind.Object;
                flag = flag2;
            }

            if (flag)
            {
                tags["unity.turnStart." + text2] = ((value4.ValueKind == JsonValueKind.True) ? "true" : "false");
            }
        }
    }

    private static void AddEndTurnTags(IDictionary<string, string> tags, JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("endTurnButton", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string[] array = new string[9]
        {
            "exists",
            "hasEndTurnOption",
            "isDisabled",
            "inputBlockedInternally",
            "isInputBlocked",
            "isInWaitingState",
            "isInNmpState",
            "isInYouHavePlaysState",
            "hasNoMorePlays"
        };
        foreach (string text in array)
        {
            bool flag = value.TryGetProperty(text, out var value2);
            if (flag)
            {
                JsonValueKind valueKind = value2.ValueKind;
                bool flag2 = valueKind - 5 <= JsonValueKind.Object;
                flag = flag2;
            }

            if (flag)
            {
                tags["unity.endTurn." + text] = ((value2.ValueKind == JsonValueKind.True) ? "true" : "false");
            }
        }

        if (value.TryGetProperty("optionIndex", out var value3) && value3.ValueKind == JsonValueKind.Number && value3.TryGetInt32(out var value4))
        {
            tags["unity.endTurn.optionIndex"] = value4.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static bool TryReadTurnTimerFriendly(JsonElement data, out bool friendlyTurn)
    {
        friendlyTurn = false;
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("turnTimer", out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("friendlySidePlayer", out var value2))
        {
            return false;
        }

        JsonValueKind valueKind = value2.ValueKind;
        if (valueKind - 5 <= JsonValueKind.Object)
        {
            friendlyTurn = value2.ValueKind == JsonValueKind.True;
            return true;
        }

        return false;
    }

    private static bool HasEndTurnOption(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("endTurnButton", out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (GetBool(value, "hasEndTurnOption") && !GetBool(value, "isDisabled") && !GetBool(value, "inputBlockedInternally"))
        {
            return !GetBool(value, "isInputBlocked");
        }

        return false;
    }

    private static bool HasReliableConstructedEndGameScreen(JsonElement data)
    {
        if (!GetBool(data, "hasConstructedEndGameScreen"))
        {
            return false;
        }

        if (GetBool(data, "hasMatchingPopup") || GetBool(data, "hasMulliganConfirmButton") || GetBool(data, "isMulliganWaitingForUserInput"))
        {
            return false;
        }

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("constructedEndGameScreen", out var value) && value.ValueKind == JsonValueKind.Object)
        {
            ContainsAnyText(GetString(value, "name") + " " + GetString(value, "path"), "VictoryTwoScoop", "DefeatTwoScoop", "EndGameTwoScoop", "RankChangeTwoScoop");
            return true;
        }

        return true;
    }

    private bool HasLocalPlayableResponse(IReadOnlyList<UnityCard> cards)
    {
        int localPlayerId = DetectLocalPlayerId(cards);
        if (localPlayerId != 0)
        {
            return cards.Any((UnityCard card) => card.PlayerId == localPlayerId && card.HasResponse == true && card.IsValidOption != false && !IsHardClientPlayError(card.PlayError));
        }

        return false;
    }

    private static bool IsHardClientPlayError(string? playError)
    {
        if (!string.IsNullOrWhiteSpace(playError) && !playError.Equals("NONE", StringComparison.OrdinalIgnoreCase))
        {
            if (!playError.Contains("REQ_YOUR_TURN", StringComparison.OrdinalIgnoreCase) && !playError.Contains("REQ_ENOUGH_MANA", StringComparison.OrdinalIgnoreCase) && !playError.Contains("REQ_MINION_TARGET", StringComparison.OrdinalIgnoreCase))
            {
                return playError.Contains("REQ_TARGET_TO_PLAY", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        return false;
    }

    private int DetectLocalPlayerId(IReadOnlyList<UnityCard> cards)
    {
        int? num = ((IEnumerable<IGrouping<int, UnityCard>>)(
            from card in cards
            where IsUnityZone(card, "HAND")group card by card.PlayerId into @group
                orderby @group.Count()descending
                select @group)).Select((Func<IGrouping<int, UnityCard>, int?>)((IGrouping<int, UnityCard> group) => group.Key)).FirstOrDefault();
        if (num.HasValue)
        {
            return num.Value;
        }

        int? num2 = ((IEnumerable<IGrouping<int, UnityCard>>)(
            from card in cards
            where card.HasResponse == true
            group card by card.PlayerId into @group
                orderby @group.Count()descending
                select @group)).Select((Func<IGrouping<int, UnityCard>, int?>)((IGrouping<int, UnityCard> group) => group.Key)).FirstOrDefault();
        if (num2.HasValue)
        {
            return num2.Value;
        }

        return cards.Where((UnityCard card) => IsUnityZone(card, "PLAY")).Where(IsHero).Select((Func<UnityCard, int?>)((UnityCard card) => card.PlayerId)).FirstOrDefault().GetValueOrDefault();
    }

    private int DetectOpponentPlayerId(IReadOnlyList<UnityCard> cards, int localPlayerId)
    {
        return (
            from card in cards
            where card.PlayerId != localPlayerId
            where IsUnityZone(card, "PLAY")select card).Where(IsHero).Select((Func<UnityCard, int?>)((UnityCard card) => card.PlayerId)).FirstOrDefault() ?? ((IEnumerable<IGrouping<int, UnityCard>>)(
            from card in cards
            where card.PlayerId != localPlayerId
            group card by card.PlayerId into @group
                orderby @group.Count()descending
                select @group)).Select((Func<IGrouping<int, UnityCard>, int?>)((IGrouping<int, UnityCard> group) => group.Key)).FirstOrDefault().GetValueOrDefault();
    }

    private static bool IsGameplayCard(UnityCard card)
    {
        if (!string.IsNullOrWhiteSpace(card.EntityId) && !card.CardId.Contains("FXWatcher", StringComparison.OrdinalIgnoreCase) && !card.CardId.Contains("_PH", StringComparison.OrdinalIgnoreCase))
        {
            return !card.Name.EndsWith("[DNT]", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool IsBoardEntity(UnityCard card)
    {
        if (!IsGameplayCard(card) || IsHero(card) || IsWeapon(card) || IsHeroPower(card))
        {
            return false;
        }

        if (!card.IsMinion && card.Attack <= 0 && card.Health <= 0)
        {
            if (_cardDatabase.TryGetByCardId(card.CardId, out HearthstoneCardMetadata metadata))
            {
                if (!string.Equals(metadata.Type, "MINION", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Equals(metadata.Type, "LOCATION", StringComparison.OrdinalIgnoreCase);
                }

                return true;
            }

            return false;
        }

        return true;
    }

    private bool IsHero(UnityCard card)
    {
        if (IsHeroPower(card))
        {
            return false;
        }

        if (card.IsMinion || card.IsSpell || card.IsWeapon)
        {
            return false;
        }

        if (_cardDatabase.TryGetByCardId(card.CardId, out HearthstoneCardMetadata metadata))
        {
            return string.Equals(metadata.Type, "HERO", StringComparison.OrdinalIgnoreCase);
        }

        if (!card.IsHero)
        {
            return IsHeroCardId(card.CardId);
        }

        return true;
    }

    private bool IsWeapon(UnityCard card)
    {
        if (!card.IsWeapon)
        {
            if (_cardDatabase.TryGetByCardId(card.CardId, out HearthstoneCardMetadata metadata))
            {
                return IsCardType(metadata, "WEAPON");
            }

            return false;
        }

        return true;
    }

    private bool IsHeroPower(UnityCard card)
    {
        if (!card.IsHeroPower)
        {
            if (_cardDatabase.TryGetByCardId(card.CardId, out HearthstoneCardMetadata metadata))
            {
                return IsCardType(metadata, "HERO_POWER");
            }

            return false;
        }

        return true;
    }

    private bool HasAnyHero(IEnumerable<UnityCard> cards)
    {
        return cards.Any(IsHero);
    }

    private static bool IsHeroCardId(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId) || cardId.Contains("HERO_POWER", StringComparison.OrdinalIgnoreCase) || cardId.EndsWith("bp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!cardId.StartsWith("HERO_", StringComparison.OrdinalIgnoreCase))
        {
            return cardId.Contains("_HERO_", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private bool IsAttackOption(UnityCard card)
    {
        if (card.HasResponse == true && IsUnityZone(card, "PLAY"))
        {
            if (card.Attack <= 0)
            {
                return IsHero(card);
            }

            return true;
        }

        return false;
    }

    private static bool HasMechanic(HearthstoneCardMetadata? metadata, params string[] mechanics)
    {
        return metadata?.Mechanics.Any((string actual) => mechanics.Any((string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))) ?? false;
    }

    private static string ReadClass(ConstructedEntity? hero)
    {
        if ((object)hero == null)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(hero.CardClass))
        {
            return hero.CardClass;
        }

        string text = hero.Name + " " + hero.CardId;
        string[] array = new string[11]
        {
            "DEMONHUNTER",
            "DRUID",
            "HUNTER",
            "MAGE",
            "PALADIN",
            "PRIEST",
            "ROGUE",
            "SHAMAN",
            "WARLOCK",
            "WARRIOR",
            "DEATHKNIGHT"
        };
        foreach (string text2 in array)
        {
            if (text.Contains(text2, StringComparison.OrdinalIgnoreCase))
            {
                return text2;
            }
        }

        return hero.Name;
    }

    private static int ResolveTurn(JsonElement data, int manaTotal)
    {
        int num = GetInt(data, "gameTurn");
        if (num > 0)
        {
            return num;
        }

        return manaTotal;
    }

    private static int ReadIntTag(ConstructedEntity? entity, string key)
    {
        if ((object)entity == null || !entity.Tags.TryGetValue(key, out string value))
        {
            return 0;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildEntityParameters(ConstructedEntity entity)
    {
        return new Dictionary<string, string>(entity.Tags, StringComparer.OrdinalIgnoreCase)
        {
            ["cost"] = entity.Cost.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyDictionary<string, string> BuildTargetedParameters(ConstructedEntity source, ConstructedEntity target)
    {
        Dictionary<string, string> dictionary = new Dictionary<string, string>(source.Tags, StringComparer.OrdinalIgnoreCase)
        {
            ["cost"] = source.Cost.ToString(CultureInfo.InvariantCulture)
        };
        AddTargetParameter(dictionary, target, "unity.path", "unity.targetPath");
        AddTargetParameter(dictionary, target, "unity.instanceId", "unity.targetInstanceId");
        AddTargetParameter(dictionary, target, "unity.cardPath", "unity.targetCardPath");
        AddTargetParameter(dictionary, target, "unity.cardInstanceId", "unity.targetCardInstanceId");
        AddTargetParameter(dictionary, target, "unity.name", "unity.targetName");
        return dictionary;
    }

    private static void AddTargetParameter(IDictionary<string, string> parameters, ConstructedEntity target, string sourceKey, string targetKey)
    {
        if (target.Tags.TryGetValue(sourceKey, out string value) && !string.IsNullOrWhiteSpace(value))
        {
            parameters[targetKey] = value;
        }
    }

    private static string ReadTag(ConstructedEntity entity, string key)
    {
        if (!entity.Tags.TryGetValue(key, out string value))
        {
            return "";
        }

        return value;
    }

    private static bool IsUnityZone(UnityCard card, string zone)
    {
        return string.Equals(card.UnityZone, zone, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetString(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static int GetInt(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
        {
            return 0;
        }

        return value2;
    }

    private static bool GetBool(JsonElement item, string property)
    {
        if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static int ReadNestedInt(JsonElement data, string objectProperty, string numberProperty, int fallback)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(objectProperty, out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(numberProperty, out var value2) || value2.ValueKind != JsonValueKind.Number || !value2.TryGetInt32(out var value3))
        {
            return fallback;
        }

        return value3;
    }

    private static bool ReadNestedBool(JsonElement data, string objectProperty, string boolProperty, bool fallback)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(objectProperty, out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(boolProperty, out var value2))
        {
            return fallback;
        }

        bool result;
        return value2.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value2.GetString(), out result) ? result : fallback,
            _ => fallback,
        };
    }

    private static bool GetEntityBool(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("entity", out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var value2))
        {
            return false;
        }

        bool result;
        return value2.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(value2.GetString(), out result) & result,
            _ => false,
        };
    }

    private static bool? GetOptionalEntityBool(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("entity", out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var value2))
        {
            return null;
        }

        switch (value2.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.String:
            {
                if (bool.TryParse(value2.GetString(), out var result))
                {
                    return result;
                }

                break;
            }
        }

        return null;
    }

    private static string? GetEntityString(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("entity", out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var value2))
        {
            return null;
        }

        return value2.ValueKind switch
        {
            JsonValueKind.String => value2.GetString(),
            JsonValueKind.Number => value2.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static int GetEntityInt(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("entity", out var value) || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out var value2))
        {
            return 0;
        }

        if (value2.ValueKind == JsonValueKind.Number && value2.TryGetInt32(out var value3))
        {
            return value3;
        }

        if (value2.ValueKind != JsonValueKind.String || !int.TryParse(value2.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }
}
