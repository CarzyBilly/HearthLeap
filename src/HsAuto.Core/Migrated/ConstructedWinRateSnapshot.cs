// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Strategy;
public sealed record ConstructedWinRateSnapshot(string MatchId, double Percent, DateTimeOffset CapturedAt, int TurnId);