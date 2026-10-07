// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HsAuto.Core.Models;

namespace HsAuto.Core.Configuration;
public static class ConstructedDeckRotationPolicy
{
    public static bool IsPlayableCatalogEntry(JsonElement deck)
    {
        if (deck.TryGetProperty("isPlayable", out var value) && value.ValueKind == JsonValueKind.True && deck.TryGetProperty("cardCount", out var value2) && value2.ValueKind == JsonValueKind.Number && value2.TryGetInt32(out var value3) && deck.TryGetProperty("minimumCardCount", out var value4) && value4.ValueKind == JsonValueKind.Number && value4.TryGetInt32(out var value5) && deck.TryGetProperty("maximumCardCount", out var value6) && value6.ValueKind == JsonValueKind.Number && value6.TryGetInt32(out var value7) && value5 > 0 && value7 >= value5 && value3 >= value5)
        {
            return value3 <= value7;
        }

        return false;
    }

    public static IReadOnlyList<ConstructedRotationDeck> InterleaveFormats(IEnumerable<ConstructedRotationDeck> decks)
    {
        ArgumentNullException.ThrowIfNull(decks, "decks");
        ConstructedRotationDeck[] array = Normalize(decks, ConstructedFormat.Standard);
        HashSet<string> standardNames = array.Select((ConstructedRotationDeck deck) => deck.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ConstructedRotationDeck[] array2 = (
            from deck in Normalize(decks, ConstructedFormat.Wild)
            where !standardNames.Contains(deck.Name)select deck).ToArray();
        List<ConstructedRotationDeck> list = new List<ConstructedRotationDeck>(array.Length + array2.Length);
        for (int num = 0; num < Math.Max(array.Length, array2.Length); num++)
        {
            if (num < array.Length)
            {
                list.Add(array[num]);
            }

            if (num < array2.Length)
            {
                list.Add(array2[num]);
            }
        }

        return list;
    }

    public static ConstructedRotationDeck? SelectNext(IReadOnlyList<ConstructedRotationDeck> orderedDecks, ConstructedFormat currentFormat, string? currentDeckName)
    {
        ArgumentNullException.ThrowIfNull(orderedDecks, "orderedDecks");
        if (orderedDecks.Count < 2)
        {
            return null;
        }

        int num = Enumerable.Range(0, orderedDecks.Count).FirstOrDefault((int index) => orderedDecks[index].Format == currentFormat && string.Equals(orderedDecks[index].Name, currentDeckName?.Trim(), StringComparison.OrdinalIgnoreCase), -1);
        if (num >= 0)
        {
            for (int num2 = 1; num2 < orderedDecks.Count; num2++)
            {
                ConstructedRotationDeck constructedRotationDeck = orderedDecks[(num + num2) % orderedDecks.Count];
                if (!string.Equals(constructedRotationDeck.Name, currentDeckName?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return constructedRotationDeck;
                }
            }

            return null;
        }

        return orderedDecks.FirstOrDefault((ConstructedRotationDeck deck) => deck.Format != currentFormat && !string.Equals(deck.Name, currentDeckName?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? orderedDecks.FirstOrDefault((ConstructedRotationDeck deck) => !string.Equals(deck.Name, currentDeckName?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static ConstructedRotationDeck[] Normalize(IEnumerable<ConstructedRotationDeck> decks, ConstructedFormat format)
    {
        return (
            from deck in decks
            where deck.Format == format && !string.IsNullOrWhiteSpace(deck.Name)select deck with
            {
                Name = deck.Name.Trim()
            }

        ).DistinctBy((ConstructedRotationDeck deck) => deck.Name, StringComparer.OrdinalIgnoreCase).OrderBy((ConstructedRotationDeck deck) => deck.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}