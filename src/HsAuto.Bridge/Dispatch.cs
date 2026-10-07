using System;
namespace HsAuto.UnityBridge { public sealed partial class OpenBridgePlugin {
private BridgeResponse HandleOpenRequest(BridgeRequest request) {
 if(request.Command.Equals("invokeMethod",StringComparison.OrdinalIgnoreCase)) {
  var method=GetString(request.ArgumentsJson,"method");
  var allowed=new[]{"TriggerTap","TriggerPress","TriggerRelease","OnRelease","OnPress","OnClick","OnMouseUp","ConfirmMulligan","OnPlayButton","OnClickPlayButton","OnConfirm","Dismiss"};
  if(Array.IndexOf(allowed,method)<0) return BridgeResponse.Failure("Method not allowed: "+method);
 }
 switch(request.Command.ToLowerInvariant()) {case "battlegroundsstatelite": return BridgeResponse.Success(BuildBattlegroundsStateLite(request));
case "accountdetails": return BridgeResponse.Success(BuildAccountDetails(request));
case "playeridentity": return BridgeResponse.Success(BuildPlayerIdentity());
case "constructedendturn": return ConstructedEndTurn();
case "constructedconcede": return ConstructedConcede(request);
case "constructedtargetedaction": return ConstructedTargetedAction(request);
case "constructedoptionaction": return ConstructedOptionAction(request);
case "constructedcancelinput": return ConstructedCancelInput();
case "choicecards": return ReadFriendlyChoiceCards();
case "selectfriendlychoice": return SelectFriendlyChoice(request);
case "cleanupfriendlychoicevisuals": return CleanupFriendlyChoiceVisuals();
case "selectmulligancard": return SelectMulliganCard(request);
case "confirmmulliganhero": return ConfirmMulliganHero(request);
case "selectmulliganhero": return SelectMulliganHero(request);
case "clickobject": return ClickObject(request);
case "invokemethod": return InvokeMethodOnObject(request);
case "constructedlobbystate": return BridgeResponse.Success(BuildConstructedLobbyState());
case "constructeddecks": return ConstructedDecks(request);
case "constructedvisibledeckcatalog": return ConstructedDeckCatalog();
case "constructedselectdeck": return ConstructedSelectDeck(request);
case "constructedswitchformat": return ConstructedSwitchFormat(request);
case "constructedopenformatpicker": return ConstructedOpenFormatPicker();
case "mainmenustate": return BridgeResponse.Success(ReadMainMenuReadiness());
case "matchmakingstate": return BridgeResponse.Success(BuildMatchmakingState());
case "reconnectstate": return BridgeResponse.Success(BuildReconnectState());
case "attemptreconnect": return AttemptReconnect();
case "continueconstructedendgame": return ContinueConstructedEndGame();
case "dismissstartscreen": return DismissStartScreen(request);
case "dismissstartuppopup": return DismissStartupPopup(request);
case "dismissrewardpopup": return DismissRewardPopup(request);
case "dismissrewardscrollpopup": return DismissRewardScrollPopup();
case "dismissnavigationpopup": return DismissNavigationPopup(request);
case "dismissingamemessagemodal": return DismissInGameMessageModal();
case "cancelmatchmaking": return CancelMatchmaking(request);
case "regiondiagnostics": return BridgeResponse.Success(BuildRegionDiagnostics());
case "returningplayerstate": return BridgeResponse.Success(ReadReturningPlayerState());
case "advancereturningplayerstartup": return AdvanceReturningPlayerStartup(request);
case "queststatus": return BridgeResponse.Success(BuildQuestStatus(true));
case "questavailability": return BridgeResponse.Success(BuildQuestStatus(false));
case "confirmfriendlychoice": return ConfirmFriendlyChoice(false);
case "find": return BridgeResponse.Success(FindObjects(request));
case "networkresponse": return InvokeNetworkResponse(request);
case "projectobject": return ProjectObject(request);
case "battlegroundsendgamestate": return BridgeResponse.Success(BuildBattlegroundsEndGameState());
case "continuebattlegroundsendgame": return ContinueBattlegroundsEndGame(request);
default: return BridgeResponse.Failure("Unsupported open-source command: "+request.Command);
}
}
}}
