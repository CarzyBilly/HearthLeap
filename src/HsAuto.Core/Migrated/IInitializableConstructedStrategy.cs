// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Strategy;
public interface IInitializableConstructedStrategy
{
    event Action<string>? Log;
    Task InitializeAsync(ConstructedStrategyRuntimeContext context, CancellationToken cancellationToken);
}