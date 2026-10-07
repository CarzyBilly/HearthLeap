// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Text.Json.Serialization;

namespace HsAuto.Core.Strategy;
public sealed record NeteaseBoxWinRateTurn
{
    [JsonPropertyName("turnId")]
    public int TurnId { get; init; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = "";

    [JsonPropertyName("winRate")]
    public double WinRate { get; init; } = double.NaN;
}