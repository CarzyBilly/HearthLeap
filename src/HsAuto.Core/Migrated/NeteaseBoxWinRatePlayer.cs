// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Text.Json.Serialization;

namespace HsAuto.Core.Strategy;
public sealed record NeteaseBoxWinRatePlayer
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("job")]
    public int Job { get; init; }
}