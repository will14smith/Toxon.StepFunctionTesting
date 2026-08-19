namespace Toxon.StepFunctionTesting.Framework;

public sealed class StepFunctionRunnerOptions
{
    public bool RequireMocks { get; init; }
    public string? RoleArn { get; init; }
    public bool SkipWaitStates { get; init; }

    /// <summary>
    /// Name used for <c>$$.StateMachine.Name</c> (and the derived ARN) in the simulated Context Object.
    /// Defaults to <c>"StateMachine"</c> when not set.
    /// </summary>
    public string? StateMachineName { get; init; }

    /// <summary>
    /// Name used for <c>$$.Execution.Name</c> (and the derived ARN) in the simulated Context Object.
    /// Defaults to a random GUID per run when not set.
    /// </summary>
    public string? ExecutionName { get; init; }
}
