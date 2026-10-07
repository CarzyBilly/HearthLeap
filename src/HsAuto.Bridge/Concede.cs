using System;
namespace HsAuto.UnityBridge {
public sealed partial class OpenBridgePlugin {
    // Explicit local user setting only; no server state/rank modifications.
    private BridgeResponse ConstructedConcede(BridgeRequest request)
    {
        try
        {
            object gameState = GetGameState();
            if (gameState == null || !IsActiveGameInProgress()) return BridgeResponse.Failure("No active game to concede.");
            bool requested = TryInvokeBool(gameState, "WasConcedeRequested");
            if (requested) return BridgeResponse.Success(new { accepted = true, alreadyRequested = true });
            if (GetBool(request.ArgumentsJson, "dryRun", false)) return BridgeResponse.Success(new { accepted = true, dryRun = true });
            if (!TryInvokeZeroArgMethod(gameState, "Concede", out var method, out var error)) return BridgeResponse.Failure(error);
            return BridgeResponse.Success(new { accepted = true, afterWasConcedeRequested = TryInvokeBool(gameState, "WasConcedeRequested"), method });
        }
        catch (Exception ex) { return BridgeResponse.Failure(FlattenInvocationException(ex)); }
    }
}
}
