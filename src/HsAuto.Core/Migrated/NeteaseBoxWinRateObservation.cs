// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;

namespace HsAuto.Core.Strategy;
public sealed record NeteaseBoxWinRateObservation(NeteaseBoxWinRatePayload Payload, DateTimeOffset CapturedAt, int RendererProcessId, ulong Address);