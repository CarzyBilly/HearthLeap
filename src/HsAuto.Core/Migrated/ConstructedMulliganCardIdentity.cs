// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using HsAuto.Core.Models;

namespace HsAuto.Core.Automation;
internal static class ConstructedMulliganCardIdentity
{
    internal static bool IsCoin(ConstructedEntity card)
    {
        if (!card.CardId.Equals("GAME_005", StringComparison.OrdinalIgnoreCase) && !card.Name.Equals("幸运币", StringComparison.Ordinal))
        {
            return card.Name.Equals("The Coin", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}