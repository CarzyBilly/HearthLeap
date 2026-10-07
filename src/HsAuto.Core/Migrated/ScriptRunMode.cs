// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Configuration;
public static class ScriptRunMode
{
    public const string Battlegrounds = "Battlegrounds";
    public const string Constructed = "Constructed";
    public const string NormalArena = "ArenaNormal";
    public const string UndergroundArena = "ArenaUnderground";
    public static bool IsConstructed(string? value)
    {
        if (!string.Equals(value, "Constructed", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(value, "传统对战", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public static bool IsArena(string? value)
    {
        ArenaVariant variant;
        return TryGetArenaVariant(value, out variant);
    }

    public static bool TryGetArenaVariant(string? value, out ArenaVariant variant)
    {
        string text = (value ?? "").Trim();
        if (text.Equals("ArenaUnderground", StringComparison.OrdinalIgnoreCase) || text.Equals("UndergroundArena", StringComparison.OrdinalIgnoreCase) || text.Equals("地下竞技场", StringComparison.OrdinalIgnoreCase))
        {
            variant = ArenaVariant.Underground;
            return true;
        }

        if (text.Equals("ArenaNormal", StringComparison.OrdinalIgnoreCase) || text.Equals("Arena", StringComparison.OrdinalIgnoreCase) || text.Equals("NormalArena", StringComparison.OrdinalIgnoreCase) || text.Equals("地上竞技场", StringComparison.OrdinalIgnoreCase))
        {
            variant = ArenaVariant.Normal;
            return true;
        }

        variant = ArenaVariant.Normal;
        return false;
    }

    public static string FromArenaVariant(ArenaVariant variant)
    {
        if (variant != ArenaVariant.Underground)
        {
            return "ArenaNormal";
        }

        return "ArenaUnderground";
    }
}