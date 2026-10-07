using System;
using System.Text.Json;

namespace HsAuto.Core.Automation;

public sealed record UnityBridgeCallTiming(string PipeName, string Command, bool Ok, TimeSpan Elapsed, int ResponseBytes, string? Error, long? StateBuildMs = null, int? GameFrame = null, int? EntityMapCount = null, JsonElement? StateBuildPhases = null);
