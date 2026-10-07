// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Models;
public sealed record ConstructedEntity
{
    public string EntityId { get; init; } = "";
    public string CardId { get; init; } = "";
    public int DbfId { get; init; }
    public string Name { get; init; } = "";
    public string RulesText { get; init; } = "";
    public string CardType { get; init; } = "";
    public string CardClass { get; init; } = "";
    public string CardSet { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string SpellSchool { get; init; } = "";
    public int BaseCost { get; init; }
    public int BaseAttack { get; init; }
    public int BaseHealth { get; init; }
    public int BaseDurability { get; init; }
    public int SpellDamage { get; init; }
    public IReadOnlyList<string> Mechanics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Races { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferencedTags { get; init; } = Array.Empty<string>();
    public ConstructedZone Zone { get; init; }
    public int Cost { get; init; }
    public int Attack { get; init; }
    public int Health { get; init; }
    public int Damage { get; init; }
    public bool CanAttack { get; init; }
    public bool HasTaunt { get; init; }
    public bool HasDivineShield { get; init; }
    public bool HasRush { get; init; }
    public bool HasCharge { get; init; }
    public bool HasStealth { get; init; }
    public bool CantBeTargetedBySpellsOrHeroPowers { get; init; }
    public bool HasImmune { get; init; }
    public bool IsSpell { get; init; }
    public bool IsMinion { get; init; }
    public bool IsWeapon { get; init; }
    public bool IsHeroPower { get; init; }
    public bool IsLocation { get; init; }
    public bool IsTradeable { get; init; }
    public int LocationDurability { get; init; }
    public int LocationCooldown { get; init; }
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
}