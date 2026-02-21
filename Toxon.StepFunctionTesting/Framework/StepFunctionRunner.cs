using System.Collections.Immutable;
using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public partial class StepFunctionRunner(
    IAmazonStepFunctions stepFunctions,
    string stateMachineDefinition,
    StepFunctionRunnerOptions options)
{
    private const string DefaultQueryLanguage = "JSONPath";
    
    public async Task<StepFunctionStateResult> RunAsync(
        string input,
        IReadOnlyDictionary<string, IMockProvider> mocks,
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
        
        var (_, result) = await RunToCompletionAsync(executionContext, stateContext, cancellationToken);

        return result;
    }

    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> RunToCompletionAsync(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
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

            return (executionContext, new StepFunctionStateResult.Success(nextStateName, stateContext.Input, stateContext.Variables, new InspectionData()));
        }
        
        // Map & Parallel states aren't supported by TestState, so they need special handling
        if (stateElement.IsMapState())
        {
            return await ExecuteMapState(executionContext, stateContext, cancellationToken);
        }
        
        if (stateElement.IsParallelState())
        {
            return await ExecuteParallelState(executionContext, stateContext, cancellationToken);
        }
        
        return await TestState(executionContext, stateContext, cancellationToken);
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
            Definition = executionContext.RootElement.GetRawText(),
            
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
                request.Context = $"{{\"Task\": {{\"Token\": \"{Guid.NewGuid()}\"}}}}";
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
    
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ExecutePassStateForJsonPath(StepFunctionExecutionContext executionContext, string path, string input, string variables, CancellationToken cancellationToken = default)
    {
        const string stateName = "$$VirtualItemSelector";
        var stateContext = new StepFunctionStateContext(stateName, "JSONPath", input, variables);
        var definition = $"{{ \"StartAt\": \"{stateName}\", \"States\": {{ \"{stateName}\": {{ \"Type\": \"Pass\", \"End\": true, \"InputPath\": \"{path}\" }} }}, \"QueryLanguage\": \"JSONPath\" }}";
        
        using var document = JsonDocument.Parse(definition);
        var branchExecutionContext = executionContext with { RootElement = document.RootElement };
        
        (branchExecutionContext, var result) = await TestState(branchExecutionContext, stateContext, null, cancellationToken);

        return (branchExecutionContext with { RootElement = executionContext.RootElement }, result);
    }
    
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ExecutePassStateForJsonAta(StepFunctionExecutionContext executionContext, JsonElement expression, string input, string variables, CancellationToken cancellationToken = default)
    {
        const string stateName = "$$VirtualItemSelector";
        var stateContext = new StepFunctionStateContext(stateName, "JSONata", input, variables);
        var definition = $"{{ \"StartAt\": \"{stateName}\", \"States\": {{ \"{stateName}\": {{ \"Type\": \"Pass\", \"End\": true, \"Output\": {expression.GetRawText()} }} }}, \"QueryLanguage\": \"JSONata\" }}";
        
        using var document = JsonDocument.Parse(definition);
        var branchExecutionContext = executionContext with { RootElement = document.RootElement };
        
        (branchExecutionContext, var result) = await TestState(branchExecutionContext, stateContext, null, cancellationToken);

        return (branchExecutionContext with { RootElement = executionContext.RootElement }, result);
    }
}