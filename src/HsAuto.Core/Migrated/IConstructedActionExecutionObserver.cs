// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using HsAuto.Core.Models;

namespace HsAuto.Core.Strategy;
public interface IConstructedActionExecutionObserver
{
    void OnActionExecutionSucceeded(ConstructedGameState state, ConstructedAction action);
}