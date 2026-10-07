// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using HsAuto.Core.Models;

namespace HsAuto.Core.Automation;
public sealed record ConstructedActionExecutionResult(bool Attempted, bool Succeeded, ConstructedAction? Action, string Reason)
{
    public static ConstructedActionExecutionResult NoAction(string reason)
    {
        return new ConstructedActionExecutionResult(Attempted: false, Succeeded: false, null, reason);
    }

    public static ConstructedActionExecutionResult Success(ConstructedAction action)
    {
        return new ConstructedActionExecutionResult(Attempted: true, Succeeded: true, action, "执行成功");
    }

    public static ConstructedActionExecutionResult Failed(ConstructedAction action, string reason)
    {
        return new ConstructedActionExecutionResult(Attempted: true, Succeeded: false, action, reason);
    }
}