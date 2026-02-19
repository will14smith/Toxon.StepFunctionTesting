using System.Collections.Immutable;
using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public class StepFunctionRunner(
    IAmazonStepFunctions stepFunctions,
    string stateMachineDefinition,
    Framework.StepFunctionRunnerOptions options)
{
    private const string DefaultQueryLanguage = "JSONPath";
    
    public async Task<StepFunctionStateResult> RunAsync(
        string input,
        IReadOnlyDictionary<string, Framework.IMockProvider> mocks,
        string? startAt = null,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(stateMachineDefinition);
        var root = document.RootElement;
        
        var executionContext = new StepFunctionExecutionContext(
            root,
            mocks,
            ImmutableDictionary<string, int>.Empty);
        
        startAt ??= root.GetProperty("StartAt").GetString() ?? throw new InvalidOperationException("State machine definition must contain a StartAt property.");
        
        var startState = executionContext.GetStateElement(startAt);
        var stateContext = new StepFunctionStateContext(
            startAt,
            startState.GetQueryLanguage() ?? executionContext.QueryLanguage ?? DefaultQueryLanguage,
            input,
            "{}"
        );
        
        var (_, result) = await RunAsync(executionContext, stateContext, cancellationToken);

        return result;
    }

    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> RunAsync(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
        // TODO run to completion instead of just one state execution
        while (true)
        {
            (executionContext, var result) = await ExecuteState(executionContext, stateContext, cancellationToken);

            switch (result)
            {
                case StepFunctionStateResult.Success success:
                    // either we're done
                    if (string.IsNullOrEmpty(success.NextStateName))
                    {
                        return (executionContext, result);
                    }
                    
                    // or we move to the next state
                    stateContext = new StepFunctionStateContext(success.NextStateName, stateContext.QueryLanguage, success.Output, success.Variables);
                    break;
                
                case StepFunctionStateResult.Failed failed:
                    // just return the failure, nothing has handled it so far so maybe something upstream will
                    return (executionContext, failed);
                    
                case StepFunctionStateResult.CaughtError caughtError:
                    // move to the next handler state
                    stateContext = new StepFunctionStateContext(caughtError.NextStateName, stateContext.QueryLanguage, caughtError.Output, caughtError.Variables);
                    break;

                case StepFunctionStateResult.Retriable:
                    // retry the same state
                    break;
                
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), null, $"Unexpected state result type: {result}.");
            }
        }
    }

    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ExecuteState(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
        // TODO handle Wait/Map/Parallel since they can't be directly ran in TestState and require special handling
        var stateName = stateContext.StateName;
        var stateElement = executionContext.GetStateElement(stateName);

        stateContext = stateContext with
        {
            QueryLanguage = stateElement.GetQueryLanguage() ?? stateContext.QueryLanguage
        };
        
        if (options.SkipWaitStates && stateElement.IsWaitState())
        {
            var nextStateName = stateElement.GetNextStateName();
            
            // TODO technically wait state could have input/output processing

            return (executionContext, new StepFunctionStateResult.Success(nextStateName, stateContext.Input, stateContext.Variables));
        }
        
        if (stateElement.IsMapState() && !executionContext.Mocks.ContainsKey(stateName))
        {
            // Maps are complex and require special handling to get the correct input/output structure for each iteration
            // Requiring a mock for maps keeps the implementation simpler and allows for more controlled testing of map behavior
            // The map item processor can be tested separately by using a custom startState
            
            throw new InvalidOperationException($"Map state '{stateName}' requires a mock provider to run.");
        }
        
        if (stateElement.IsParallelState() && !executionContext.Mocks.ContainsKey(stateName))
        {
            // Parallel states aren't supported by TestState, so if it's not mocked we need to handle it ourselves
            
            return await ExecuteParallelState(executionContext, stateContext, cancellationToken);
        }
        
        return await TestState(executionContext, stateContext, cancellationToken);
    }

    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ExecuteParallelState(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
        var stateName = stateContext.StateName;
        var stateElement = executionContext.GetStateElement(stateName);
        
        if (!stateElement.TryGetProperty("Branches", out var branchesElement) || branchesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Parallel state '{stateName}' must define a Branches array.");
        }

        var branchOutputs = new List<JsonElement>();

        foreach (var branch in branchesElement.EnumerateArray())
        {
            var branchStartAt = branch.GetProperty("StartAt").GetString() ?? throw new InvalidOperationException("Branch definition must contain a StartAt property.");
            
            var branchExecutionContext = executionContext.AsBranch(branch);
            var branchStateContext = stateContext with
            {
                StateName = branchStartAt,
                QueryLanguage = branch.GetQueryLanguage() ?? stateContext.QueryLanguage
            };
            
            (branchExecutionContext, var branchResult) = await RunAsync(branchExecutionContext, branchStateContext, cancellationToken);
            executionContext = executionContext.UpdateFromBranch(branchExecutionContext);
            
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
                    
                    return await TestState(executionContext, stateContext, errorMock, cancellationToken);
                
                default:
                    throw new ArgumentOutOfRangeException(nameof(branchResult), null, $"Unexpected branch result type: {branchResult}.");
            }
        }
        
        var output = JsonSerializer.Serialize(branchOutputs);
        var mock = new MockInput
        {
            Result = output,
            FieldValidationMode = MockResponseValidationMode.NONE
        };
        
        return await TestState(executionContext, stateContext, mock, cancellationToken);
    }

    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> TestState(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
        var stateName = stateContext.StateName;
        var attempt = CollectionExtensions.GetValueOrDefault(executionContext.Attempts, stateName, 0);

        MockInput? mock = null;
        if (executionContext.Mocks.TryGetValue(stateName, out var mockProvider))
        {
            mock = mockProvider.GetMock(attempt);
        }
        
        return await TestState(executionContext, stateContext, mock, cancellationToken);
    }
    
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> TestState(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, MockInput? mock, CancellationToken cancellationToken)
    {
        var stateName = stateContext.StateName;
        var stateElement = executionContext.GetStateElement(stateName);

        var request = new TestStateRequest
        {
            Definition = stateMachineDefinition,
            
            StateName = stateName,
            Input = stateContext.Input,
            Variables = stateContext.Variables,
            
            InspectionLevel = InspectionLevel.DEBUG,
        };
        
        var attempt = CollectionExtensions.GetValueOrDefault(executionContext.Attempts, stateName, 0);
        var newAttempts = executionContext.Attempts.SetItem(stateName, attempt + 1);
        executionContext = executionContext with { Attempts = newAttempts };
        
        if (mock != null)
        {
            request.Mock = mock;
            request.StateConfiguration ??= new TestStateConfiguration();
            request.StateConfiguration.RetrierRetryCount = attempt;

            if (stateElement.RequiresTaskToken())
            {
                request.Context = "{\"Task\": {\"Token\": \"test-token\"}}";
            }
        }
        else if (options.RequireMocks && stateElement.IsTaskState())
        {
            throw new InvalidOperationException($"State '{stateName}' requires a mock, but none was provided.");
        }
        
        var response = await stepFunctions.TestStateAsync(request, cancellationToken);
        var result = StepFunctionStateResult.FromResponse(response);
        
        return (executionContext, result);
    }
}