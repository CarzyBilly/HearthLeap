// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Models;
public sealed record ConstructedGameState
{
    public string MatchId { get; init; } = "local";
    public int Turn { get; init; }
    public ConstructedPhase Phase { get; init; }
    public ConstructedFormat Format { get; init; }
    public ConstructedGameResult Result { get; init; }
    public string LocalPlayerClass { get; init; } = "";
    public string OpponentClass { get; init; } = "";
    public ConstructedEntity? LocalHero { get; init; }
    public ConstructedEntity? OpponentHero { get; init; }
    public int ManaAvailable { get; init; }
    public int ManaTotal { get; init; }
    public int HeroHealth { get; init; } = 30;
    public int HeroArmor { get; init; }
    public int OpponentHealth { get; init; } = 30;
    public int OpponentArmor { get; init; }
    public IReadOnlyList<ConstructedEntity> Hand { get; init; } = Array.Empty<ConstructedEntity>();
    public IReadOnlyList<ConstructedEntity> FriendlyBoard { get; init; } = Array.Empty<ConstructedEntity>();
    public IReadOnlyList<ConstructedEntity> EnemyBoard { get; init; } = Array.Empty<ConstructedEntity>();
    public ConstructedEntity? FriendlyWeapon { get; init; }
    public ConstructedEntity? EnemyWeapon { get; init; }
    public ConstructedEntity? LocalHeroPower { get; init; }
    public IReadOnlyList<ConstructedEntity> LocalHeroPowers { get; init; } = Array.Empty<ConstructedEntity>();
    public int SecretsCount { get; init; }
    public IReadOnlyList<ConstructedEntity> DiscoverOptions { get; init; } = Array.Empty<ConstructedEntity>();
    public IReadOnlyList<ConstructedEntity> ChoiceOptions { get; init; } = Array.Empty<ConstructedEntity>();
    public IReadOnlyList<ConstructedAction> CurrentPlayableActions { get; init; } = Array.Empty<ConstructedAction>();
    public IReadOnlyList<ConstructedPredictorOption> PredictorOptions { get; init; } = Array.Empty<ConstructedPredictorOption>();
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<ConstructedEntity> GetLocalHeroPowers()
    {
        if (LocalHeroPowers.Count > 0)
        {
            return LocalHeroPowers;
        }

        if ((object)LocalHeroPower != null)
        {
            return new ConstructedEntity[1]
            {
                LocalHeroPower
            };
        }

        return Array.Empty<ConstructedEntity>();
    }
}