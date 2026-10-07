using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var original = args[0]; var target = args[1];
// This migration generator predates the maintained engine/UI. Never overwrite a working project.
if (File.Exists(Path.Combine(target, "src", "HsAuto.Core", "TraditionalControls.cs")))
    throw new InvalidOperationException("SourceSlice is for the initial migration only. Build the maintained source directly; regenerating would discard 0.1.1 changes.");
var parse = new CSharpParseOptions(LanguageVersion.Preview);
CompilationUnitSyntax Parse(string text) => CSharpSyntaxTree.ParseText(text, parse).GetCompilationUnitRoot();
string ReplaceMethod(string text, string name, string replacement)
{
    var root = Parse(text);
    var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().First(x => x.Identifier.Text == name);
    return root.ReplaceNode(method, SyntaxFactory.ParseMemberDeclaration(replacement)!).ToFullString();
}
var coreDir = Path.Combine(original, "HsAuto.Core");
var files = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories)
    .Where(x => !x.Contains("System.Text.RegularExpressions.Generated") && !Path.GetFileName(x).StartsWith("--") && !x.Contains("Properties") && !x.Contains("\\obj\\") && !x.Contains("\\bin\\"))
    .ToDictionary(x => Path.GetFileNameWithoutExtension(x), x => File.ReadAllText(x));
// Only the single local client selected by the new app is used. Never kill users' boxes.
files["NeteaseBoxRecommendationSource"] = ReplaceMethod(files["NeteaseBoxRecommendationSource"], "EnsureSingleNeteaseBoxProcessAsync", """
private Task<string> EnsureSingleNeteaseBoxProcessAsync(int targetProcessId, CancellationToken cancellationToken) {
 cancellationToken.ThrowIfCancellationRequested();
 using var all = new ProcessCollection(Process.GetProcessesByName("HSAng"));
 var alive = all.Processes.Where(p => !SafeHasExited(p)).ToArray();
 if (alive.Length != 1) throw new InvalidOperationException("请保留一个炉石盒子进程，再点击开始。");
 var path = TryGetProcessPath(alive[0]);
 if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("无法读取盒子路径，请保持两个软件权限一致。");
 return Task.FromResult(path);
}
""");
files["NeteaseBoxRecommendationSource"] = ReplaceMethod(files["NeteaseBoxRecommendationSource"], "StopBoxProcessAsync", """
private static Task StopBoxProcessAsync(Process current, CancellationToken cancellationToken) {
 throw new InvalidOperationException("开源初版不自动关闭盒子，请手动重新打开后重试。");
}
""");
var seeds = new[] { "UnityBridgeConstructedGameStateReader", "ConstructedActionExecutor", "NeteaseBoxConstructedStrategy", "HearthstoneConstructedNavigator" };
files["NeteaseBoxConstructedStrategy"] = files["NeteaseBoxConstructedStrategy"].Replace("_source is NeteaseDirectRecommendationSource", "false");
var selected = new HashSet<string>(seeds);
for (bool changed = true; changed;) {
 changed = false;
 foreach (var file in selected.ToArray()) {
  if (!files.TryGetValue(file, out var content)) continue;
  foreach (var id in Parse(content).DescendantTokens().Where(x => x.IsKind(SyntaxKind.IdentifierToken)).Select(x => x.ValueText))
   if (id != "UnityBridgeClient" && files.ContainsKey(id) && selected.Add(id)) changed = true;
 }
}
// Save an explicit dependency slice, not the old GUI, licence service, token login or runtime resources.
var outDir = Path.Combine(target, "src", "HsAuto.Core", "Migrated"); Directory.CreateDirectory(outDir);
foreach (var name in selected) {
 if (name.Contains("Security") || name.Contains("License")) throw new InvalidOperationException("Unexpected licence dependency: " + name);
 var root=Parse(files[name].Replace("using System.Text.RegularExpressions.Generated;", ""));
 var regexMethods=root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>m.AttributeLists.ToString().Contains("GeneratedRegex")).ToArray();
 if(regexMethods.Length>0) {
  root=root.ReplaceNodes(regexMethods,(m,_)=>m.WithModifiers(m.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword))).WithBody(null).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));
  root=root.ReplaceNodes(root.DescendantNodes().OfType<ClassDeclarationSyntax>(),(c,_)=>c.WithModifiers(c.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword))));
 }
 File.WriteAllText(Path.Combine(outDir, name + ".cs"), "// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.\n" + root.NormalizeWhitespace().ToFullString());
}
Console.WriteLine("Core slice: " + selected.Count + " files");

var bridgeDir = Path.Combine(original, "hsmm", "HsAuto.UnityBridge");
var bridge = Parse(File.ReadAllText(Path.Combine(bridgeDir, "UnityBridgePlugin.cs")));
var cls = bridge.DescendantNodes().OfType<ClassDeclarationSyntax>().First(x => x.Identifier.Text == "UnityBridgePlugin");
var members = cls.Members.ToArray();
IEnumerable<string> Names(MemberDeclarationSyntax m) => m switch {
 MethodDeclarationSyntax x => [x.Identifier.Text],
 BaseTypeDeclarationSyntax x => [x.Identifier.Text],
 FieldDeclarationSyntax x => x.Declaration.Variables.Select(v=>v.Identifier.Text),
 PropertyDeclarationSyntax x => [x.Identifier.Text],
 _ => []
};
var commands = new Dictionary<string,string> {
 ["battlegroundsStateLite"]="BuildBattlegroundsStateLite", ["accountDetails"]="BuildAccountDetails", ["constructedEndTurn"]="ConstructedEndTurn",
 ["constructedTargetedAction"]="ConstructedTargetedAction", ["constructedOptionAction"]="ConstructedOptionAction", ["constructedCancelInput"]="ConstructedCancelInput",
 ["choiceCards"]="ReadFriendlyChoiceCards", ["selectFriendlyChoice"]="SelectFriendlyChoice", ["cleanupFriendlyChoiceVisuals"]="CleanupFriendlyChoiceVisuals",
 ["selectMulliganCard"]="SelectMulliganCard", ["confirmMulliganHero"]="ConfirmMulliganHero", ["selectMulliganHero"]="SelectMulliganHero",
 ["clickObject"]="ClickObject", ["invokeMethod"]="InvokeMethodOnObject", ["constructedLobbyState"]="BuildConstructedLobbyState",
 ["constructedDecks"]="ConstructedDecks", ["constructedVisibleDeckCatalog"]="ConstructedDeckCatalog", ["constructedSelectDeck"]="ConstructedSelectDeck",
 ["constructedSwitchFormat"]="ConstructedSwitchFormat", ["constructedOpenFormatPicker"]="ConstructedOpenFormatPicker", ["mainMenuState"]="ReadMainMenuReadiness",
 ["matchmakingState"]="BuildMatchmakingState", ["reconnectState"]="BuildReconnectState", ["attemptReconnect"]="AttemptReconnect",
 ["continueConstructedEndGame"]="ContinueConstructedEndGame", ["dismissStartScreen"]="DismissStartScreen", ["dismissStartupPopup"]="DismissStartupPopup",
 ["dismissRewardPopup"]="DismissRewardPopup", ["dismissRewardScrollPopup"]="DismissRewardScrollPopup", ["dismissNavigationPopup"]="DismissNavigationPopup",
 ["dismissInGameMessageModal"]="DismissInGameMessageModal", ["cancelMatchmaking"]="CancelMatchmaking", ["regionDiagnostics"]="BuildRegionDiagnostics",
 ["returningPlayerState"]="ReadReturningPlayerState", ["advanceReturningPlayerStartup"]="AdvanceReturningPlayerStartup",
 ["questStatus"]="BuildQuestStatus", ["questAvailability"]="BuildQuestStatus",["confirmFriendlyChoice"]="ConfirmFriendlyChoice",["find"]="FindObjects",
 ["networkResponse"]="InvokeNetworkResponse", ["projectObject"]="ProjectObject",["battlegroundsEndGameState"]="BuildBattlegroundsEndGameState",["continueBattlegroundsEndGame"]="ContinueBattlegroundsEndGame"
};
var selectedMembers = new HashSet<MemberDeclarationSyntax>();
var required = new HashSet<string>(commands.Values.Concat(new[]{"BridgeResponse", "GetString", "GetInt", "GetBool", "ExtractObject", "MainThreadCall"}));
// Supply a fresh request with no card-key/session/expiry verification.
var omit = new HashSet<string>{"BridgeRequest", "MainThreadCall", "Awake", "Start", "Update", "OnDestroy", "ServerLoop", "Dispatch", "HandleOnMainThread", "TryValidateStartupSecurity", "ValidateBridgeRequest"};
for(bool changed = true; changed;) {
 changed = false;
 foreach(var m in members) {
  if(Names(m).Any(omit.Contains) || selectedMembers.Contains(m) || !Names(m).Any(required.Contains)) continue;
  selectedMembers.Add(m); changed = true;
  foreach(var id in m.DescendantTokens().Where(x=>x.IsKind(SyntaxKind.IdentifierToken)).Select(x=>x.ValueText)) required.Add(id);
 }
}
var externalFiles = Directory.GetFiles(bridgeDir,"*.cs").Where(x=>Path.GetFileName(x)!="UnityBridgePlugin.cs").ToDictionary(x=>Path.GetFileNameWithoutExtension(x),File.ReadAllText);
var external = new HashSet<string>();
for(bool changed = true; changed;) {
 changed=false;
 foreach(var name in required.ToArray()) if(externalFiles.TryGetValue(name,out var content) && external.Add(name)) {
  changed=true;
  foreach(var id in Parse(content).DescendantTokens().Where(x=>x.IsKind(SyntaxKind.IdentifierToken)).Select(x=>x.ValueText)) required.Add(id);
 }
}
var forbidden = new[]{"AntiCheatPatchInstaller","TokenLoginPatches","StudioHardwarePatches","StudioSystemProxyPolicy","HearthstoneTrafficCapture","RuntimeAccelerationPatches","ProcessScopedUserUiExecution","AttackInputMethodCorrection"};
foreach(var f in forbidden) if(required.Contains(f)) Console.WriteLine("REMOVE dependency manually: " + f);
var pluginDir=Path.Combine(target,"src","HsAuto.Bridge");
foreach(var name in external.Where(n=>!n.StartsWith("AttackInput") && n!="ProcessScopedUserUiExecution")) File.WriteAllText(Path.Combine(pluginDir,name+".cs"),externalFiles[name]);
File.WriteAllText(Path.Combine(pluginDir,"GameAdapter.cs"), """
using System; using System.Collections; using System.Collections.Concurrent; using System.Collections.Generic;
using System.Diagnostics; using System.Globalization; using System.IO; using System.IO.Pipes; using System.Linq;
using System.Reflection; using System.Runtime.CompilerServices; using System.Text; using System.Text.RegularExpressions;
using System.Threading; using BepInEx; using BepInEx.Logging; using UnityEngine; using UnityEngine.Events;
using UnityEngine.SceneManagement; using UnityEngine.UI; using Object = UnityEngine.Object;
namespace HsAuto.UnityBridge {
public sealed partial class OpenBridgePlugin : BaseUnityPlugin {
""" + System.Text.RegularExpressions.Regex.Replace(string.Join("\n",members.Where(selectedMembers.Contains).Select(x=>x.ToFullString())),@"^\s*(ProcessScopedUserUiExecution\.(BeginAction|CompleteAction)\([^;]+;|AttackInputMethodCorrection\.CancelPendingConstructedOption\([^;]+;)","",System.Text.RegularExpressions.RegexOptions.Multiline).Replace("long token = (trackAttackCandidate ? AttackInputMethodCorrection.RegisterPendingConstructedOption(sourceEntityId, targetEntityId, holdSource) : 0);","long token = 0;") + "\n}}\n");
File.WriteAllText(Path.Combine(target,"evidence","source-slice.json"),System.Text.Json.JsonSerializer.Serialize(new{Core=selected.Order(),PluginMembers=selectedMembers.Count,External=external,Commands=commands},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
var cases=new List<string>();
foreach(var pair in commands) {
 var method=members.OfType<MethodDeclarationSyntax>().First(m=>m.Identifier.Text==pair.Value);
 var parameters=method.ParameterList.Parameters.Select(p=>p.Type?.ToString()=="BridgeRequest" ? "request" : p.Type?.ToString()=="bool" ? (pair.Key=="confirmFriendlyChoice" || pair.Key=="questAvailability" ? "false" : "true") : throw new Exception("Unknown parameter: "+p));
 var call=pair.Value+"("+string.Join(",",parameters)+")";
 cases.Add("case \""+pair.Key.ToLowerInvariant()+"\": return "+(method.ReturnType.ToString()=="BridgeResponse" ? call : "BridgeResponse.Success("+call+")")+";");
}
File.WriteAllText(Path.Combine(pluginDir,"Dispatch.cs"),"""
using System;
namespace HsAuto.UnityBridge { public sealed partial class OpenBridgePlugin {
private BridgeResponse HandleOpenRequest(BridgeRequest request) {
 if(request.Command.Equals("invokeMethod",StringComparison.OrdinalIgnoreCase)) {
  var method=GetString(request.ArgumentsJson,"method");
  var allowed=new[]{"TriggerTap","TriggerPress","TriggerRelease","OnRelease","OnPress","OnClick","OnMouseUp","ConfirmMulligan","OnPlayButton","OnClickPlayButton","OnConfirm","Dismiss"};
  if(Array.IndexOf(allowed,method)<0) return BridgeResponse.Failure("Method not allowed: "+method);
 }
 switch(request.Command.ToLowerInvariant()) {
"""+string.Join("\n",cases)+"\ndefault: return BridgeResponse.Failure(\"Unsupported open-source command: \"+request.Command);\n}\n}\n}}\n");
Console.WriteLine($"Plugin slice: {selectedMembers.Count} members; {string.Join(", ",external)}");
