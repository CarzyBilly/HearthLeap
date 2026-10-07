// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
namespace HsAuto.Core.Automation;
public sealed record MatchStartPreflightResult(MatchStartPreflightDecision Decision, string Message)
{
    public bool ShouldLog { get; init; } = true;
    public string Diagnostic { get; init; } = "";
    public static MatchStartPreflightResult AllowStart { get; } = new MatchStartPreflightResult(MatchStartPreflightDecision.Allow, "");

    public static MatchStartPreflightResult WaitFor(string message)
    {
        return new MatchStartPreflightResult(MatchStartPreflightDecision.Wait, message);
    }

    public static MatchStartPreflightResult StopFor(string message)
    {
        return new MatchStartPreflightResult(MatchStartPreflightDecision.Stop, message);
    }
}