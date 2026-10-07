// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HsAuto.Core.Cards;
public sealed record HearthstoneCardMetadata
{
    public string Id { get; init; } = "";
    public int DbfId { get; init; }
    public string Name { get; init; } = "";
    public string Text { get; init; } = "";
    public string PlainText { get; init; } = "";
    public string Type { get; init; } = "";
    public string CardClass { get; init; } = "";
    public string Set { get; init; } = "";
    public string Rarity { get; init; } = "";
    public string SpellSchool { get; init; } = "";
    public int Cost { get; init; }
    public int Attack { get; init; }
    public int Health { get; init; }
    public int Durability { get; init; }
    public int SpellDamage { get; init; }
    public bool Collectible { get; init; }
    public IReadOnlyList<string> Mechanics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Races { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReferencedTags { get; init; } = Array.Empty<string>();

    public bool HasMechanic(string mechanic)
    {
        return Mechanics.Any((string item) => string.Equals(item, mechanic, StringComparison.OrdinalIgnoreCase));
    }
}