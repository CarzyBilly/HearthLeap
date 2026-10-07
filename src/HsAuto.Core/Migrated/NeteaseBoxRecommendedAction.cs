// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HsAuto.Core.Strategy;
public sealed record NeteaseBoxRecommendedAction
{
    [JsonPropertyName("actionName")]
    public string ActionName { get; init; } = "";

    [JsonPropertyName("card")]
    public JsonElement Card { get; init; }

    [JsonPropertyName("target")]
    public JsonElement Target { get; init; }

    [JsonPropertyName("oppTarget")]
    public JsonElement OppTarget { get; init; }

    [JsonPropertyName("targetHero")]
    public JsonElement TargetHero { get; init; }

    [JsonPropertyName("oppTargetHero")]
    public JsonElement OppTargetHero { get; init; }

    [JsonPropertyName("target-card")]
    public JsonElement HyphenTarget { get; init; }

    [JsonPropertyName("opp-target")]
    public JsonElement HyphenOppTarget { get; init; }

    [JsonPropertyName("target-hero")]
    public JsonElement HyphenTargetHero { get; init; }

    [JsonPropertyName("opp-target-hero")]
    public JsonElement HyphenOppTargetHero { get; init; }

    [JsonPropertyName("subOption")]
    public JsonElement SubOption { get; init; }

    [JsonPropertyName("position")]
    public int Position { get; init; }

    [JsonPropertyName("using_disguised")]
    public int UsingDisguised { get; init; } = -1;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}