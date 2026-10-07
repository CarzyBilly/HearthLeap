// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Strategy;
public interface IConstructedWinRateProvider
{
    bool TryGetLatestWinRate(string matchId, out ConstructedWinRateSnapshot snapshot);
    void ResetWinRate();
}