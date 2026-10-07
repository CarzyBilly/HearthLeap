// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HsAuto.Core.Configuration;
using HsAuto.Core.Models;

namespace HsAuto.Core.Strategy;
public interface IConstructedStrategy
{
    string Name { get; }

    Task<IReadOnlyList<ConstructedAction>> DecideAsync(ConstructedGameState state, ConstructedSettings settings, CancellationToken cancellationToken);
}