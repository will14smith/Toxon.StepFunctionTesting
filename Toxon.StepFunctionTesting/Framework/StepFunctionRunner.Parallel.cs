using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public partial class StepFunctionRunner
{
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ExecuteParallelState(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
        var stateName = stateContext.StateName;
        
        // if it's mocked we can just run the test state which will use the mock
        if (executionContext.Mocks.ContainsKey(stateName))
        {
            return await TestState(executionContext, stateContext, cancellationToken);
        }
        
        // otherwise:
        // 1. execute each branch and collect the results
        var stateElement = executionContext.GetStateElement(stateName);
        if (!stateElement.TryGetProperty("Branches", out var branchesElement) || branchesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Parallel state '{stateName}' must define a Branches array.");
        }

        var branchOutputs = new List<JsonElement>();

        foreach (var branch in branchesElement.EnumerateArray())
        {
            var branchStartAt = branch.GetProperty("StartAt").GetString() ?? throw new InvalidOperationException("Branch definition must contain a StartAt property.");

            var branchStateContext = stateContext with
            {
                StateName = branchStartAt,
                QueryLanguage = branch.GetQueryLanguage() ?? stateContext.QueryLanguage
            };

            (executionContext, var branchResult) = await RunToCompletionAsync(executionContext, branchStateContext, cancellationToken);

            switch (branchResult)
            {
                case StepFunctionStateResult.Success success:
                {
                    using var document = JsonDocument.Parse(success.Output);
                    branchOutputs.Add(document.RootElement.Clone());
                    break;
                }

                case StepFunctionStateResult.Failed failed:
                    var errorMock = new MockInput
                    {
                        ErrorOutput = new MockErrorOutput
                        {
                            Error = failed.Error,
                            Cause = failed.Cause,
                        }
                    };

                    // failures can be handled as a mock on the Parallel state
                    return await TestState(executionContext, stateContext, errorMock, cancellationToken);

                default:
                    throw new ArgumentOutOfRangeException(nameof(branchResult), null, $"Unexpected branch result type: {branchResult}.");
            }
        }

        // 2. set up a mock for the parallel state and run the test state (which will check any output processing, error handling, etc)
        var output = JsonSerializer.Serialize(branchOutputs);
        var mock = new MockInput
        {
            Result = output,
            FieldValidationMode = MockResponseValidationMode.NONE
        };

        return await TestState(executionContext, stateContext, mock, cancellationToken);
    }
}