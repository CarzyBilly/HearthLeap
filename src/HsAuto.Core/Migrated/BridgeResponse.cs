// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System.Text.Json;

namespace HsAuto.Core.Automation;
public sealed record BridgeResponse(bool Ok, string? Error, JsonElement Data);