namespace Toxon.StepFunctionTesting.Framework;

public record StepFunctionResult(
    StepFunctionStateResult Result,
    IReadOnlyList<StepFunctionStateInvocation> Invocations)
{
    internal static StepFunctionResult From(StepFunctionExecutionContext executionContext, StepFunctionStateResult result)
    {
        return new StepFunctionResult(result, executionContext.Invocations);
    }
}
