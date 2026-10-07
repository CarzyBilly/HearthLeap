// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HsAuto.Core.Strategy;
public sealed record NeteaseBoxWinRatePayload
{
    [JsonPropertyName("myWinRate")]
    public double MyWinRate { get; init; } = double.NaN;

    [JsonPropertyName("myPlayer")]
    public NeteaseBoxWinRatePlayer? MyPlayer { get; init; }

    [JsonPropertyName("oppPlayer")]
    public NeteaseBoxWinRatePlayer? OpponentPlayer { get; init; }

    [JsonPropertyName("turns")]
    public IReadOnlyList<NeteaseBoxWinRateTurn> Turns { get; init; } = Array.Empty<NeteaseBoxWinRateTurn>();
}