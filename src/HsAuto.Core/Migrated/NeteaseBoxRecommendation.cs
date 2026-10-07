// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HsAuto.Core.Strategy;
public sealed record NeteaseBoxRecommendation
{
    [JsonPropertyName("choiceId")]
    public int ChoiceId { get; init; }

    [JsonPropertyName("optionId")]
    public int OptionId { get; init; }

    [JsonPropertyName("turnNum")]
    public int TurnNum { get; init; }

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("error")]
    public string Error { get; init; } = "";

    [JsonPropertyName("data")]
    public IReadOnlyList<NeteaseBoxRecommendedAction> Data { get; init; } = Array.Empty<NeteaseBoxRecommendedAction>();
}