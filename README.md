# Toxon.StepFunctionTesting

Test AWS Step Functions state logic from .NET by running states through `TestState` with mocks.

## Install

```bash
dotnet add package Toxon.StepFunctionTesting
```

## What this library does

- Runs Step Function state transitions from a JSON definition
- Supports mock sequences for task outcomes (`ThenReturn`, `ThenFail`)
- Handles retries/catches via AWS Step Functions behavior
- Returns a `StepFunctionResult` containing the typed result (`Success`, `Failed`, `CaughtError`, `Retriable`) and state invocation history

## Prerequisites

- .NET 8+ test project
- AWS credentials available at runtime (for `states:TestState`)
- IAM permission for `states:TestState`

This library calls the AWS Step Functions `TestState` API, so tests are not fully offline.

## Quick start

```csharp
using Amazon.StepFunctions;
using Toxon.StepFunctionTesting.Framework;

var definition = """
{
  "StartAt": "Work",
  "States": {
    "Work": {
      "Type": "Task",
      "Resource": "arn:aws:states:::lambda:invoke",
      "End": true
    }
  }
}
""";

var runner = new StepFunctionRunner(
    new AmazonStepFunctionsClient(),
    definition,
    new StepFunctionRunnerOptions { RequireMocks = true });

var mocks = new Dictionary<string, IMockProvider>
{
    ["Work"] = new MockSequence()
        .ThenReturn("{\"ok\":true}")
};

var result = await runner.RunAsync("{}", mocks);

if (result.Result is StepFunctionStateResult.Success success)
{
    Console.WriteLine(success.Output);
}

foreach (var invocation in result.Invocations)
{
    Console.WriteLine($"{invocation.State.StateName}: {invocation.Result.GetType().Name}");
}
```

## Common mocking patterns

```csharp
var mocks = new Dictionary<string, IMockProvider>
{
    ["Fetch"] = new MockSequence()
        .ThenFail("RetryableError")
        .ThenReturn("{\"value\":123}"),

    ["Validate"] = new MockSequence()
        .ThenFail("ValidationError", "missing required field")
};
```

## Notes and limitations

- If `RequireMocks` is `true`, ALL task states must have mocks.
- `SkipWaitStates` is recommended for test speed, but will not evaluate any expression for wait duration or any assignments/state transformation on Wait states
- Parallel states are handled by running branches and combining outputs.
- Map states are supported for INLINE maps but not for DISTRIBUTED, however both can be mocked.
- **`$states.context.Execution.Input` limitation**: Due to AWS TestState API restrictions, execution context (including `Execution.Input`) can only be provided when a mock is supplied, and mocks can only be provided for Task/Parallel/Map states. This means:
  - `$states.context.Execution.Input` is available in mocked Task/Parallel/Map states
  - `$states.context.Execution.Input` is **not available** in Pass, Choice, Wait, Succeed, or Fail states
  - **Workaround**: Capture needed values from the original input in an initial Pass state using `Assign` before any Task states, then reference those variables throughout the execution 

## CI usage (GitHub Actions)

If you run tests in CI, ensure AWS credentials are available (for example via GitHub OIDC) and the assumed role includes `states:TestState`.
