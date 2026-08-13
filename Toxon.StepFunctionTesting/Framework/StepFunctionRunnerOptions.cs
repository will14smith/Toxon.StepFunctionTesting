namespace Toxon.StepFunctionTesting.Framework;

public sealed class StepFunctionRunnerOptions
{
    public bool RequireMocks { get; init; }
    public string? RoleArn { get; init; }
    public bool SkipWaitStates { get; init; }
    public string? ExecutionId { get; init; }
    public string? ExecutionName { get; init; }
    public string? StateMachineArn { get; init; }
}
