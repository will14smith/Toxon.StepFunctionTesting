using System.Text.Json;
using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public partial class StepFunctionRunner
{
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ExecuteMapState(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, CancellationToken cancellationToken)
    {
        var stateName = stateContext.StateName;
        
        // if it's mocked we can just run the test state which will use the mock
        if (executionContext.Mocks.ContainsKey(stateName))
        {
            return await TestState(executionContext, stateContext, cancellationToken);
        }
        
        var stateElement = executionContext.GetStateElement(stateName);
        
        // otherwise:
        // 1. calculate the items array
        (executionContext, var itemsResult) = await GetMapStateItems(executionContext, stateContext, stateElement, cancellationToken);
        if (itemsResult is not StepFunctionStateResult.Success itemsSuccess)
        {
            if (itemsResult is not StepFunctionStateResult.Failed failed)
            {
                throw new InvalidOperationException($"Unsupported result type from getting Map state items: {itemsResult}");
            }
            
            var errorMock = new MockInput
            {
                ErrorOutput = new MockErrorOutput
                {
                    Error = failed.Error,
                    Cause = failed.Cause,
                }
            };

            return await TestState(executionContext, stateContext, errorMock, cancellationToken);
        }
        
        // 2. execute the iterator for each item and collect the results
        (executionContext, var result) = await ProcessMapStateItems(executionContext, stateContext, stateElement, itemsSuccess.Output, cancellationToken);
        if (result is not StepFunctionStateResult.Success success)
        {
            if (result is not StepFunctionStateResult.Failed failed)
            {
                throw new InvalidOperationException($"Unsupported result type from processing Map state items: {result}");
            }
            
            var errorMock = new MockInput
            {
                ErrorOutput = new MockErrorOutput
                {
                    Error = failed.Error,
                    Cause = failed.Cause,
                }
            };

            return await TestState(executionContext, stateContext, errorMock, cancellationToken);
        }
        
        // 3. set up a mock for the map state and run the test state (which will check any output processing, error handling, etc)
        var mock = new MockInput
        {
            Result = success.Output,
            FieldValidationMode = MockResponseValidationMode.NONE
        };

        return await TestState(executionContext, stateContext, mock, cancellationToken);
    }
    
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> GetMapStateItems(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, JsonElement stateElement, CancellationToken cancellationToken)
    {
        (executionContext, var readerResult) = await ReadMapStateItems(executionContext, stateContext, stateElement);
        if (readerResult is not StepFunctionStateResult.Success readerSuccess)
        {
            return (executionContext, readerResult);
        }
        
        (executionContext, var selectResult) = await SelectMapStateItems(executionContext, stateContext, stateElement, readerSuccess, cancellationToken);
        if (selectResult is not StepFunctionStateResult.Success selectSuccess)
        {
            return (executionContext, selectResult);
        }
        
        return await BatchMapStateItems(executionContext, stateContext, stateElement, selectSuccess);
    }

    private static Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ReadMapStateItems(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, JsonElement stateElement)
    {
        if (stateElement.TryGetProperty("ItemReader", out _))
        {
            throw new NotImplementedException($"'ItemReader' in Map state ({stateContext.StateName}) is not currently supported.");
        }

        // default is to read from the whole state input (for both JSONPath and JSONata)
        var result = new StepFunctionStateResult.Success(null, stateContext.Input, stateContext.Variables, new InspectionData());
        
        return Task.FromResult<(StepFunctionExecutionContext, StepFunctionStateResult)>((executionContext, result));
    }
    
    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> SelectMapStateItems(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, JsonElement stateElement, StepFunctionStateResult.Success itemReaderResult, CancellationToken cancellationToken)
    {
        // selecting items:
        // JSONPath: use 'ItemsPath' if present to select items from the reader result
        // JSONata: use 'Items' if present to evaluate an expression that returns an items array  
        switch (stateContext.QueryLanguage)
        {
            case "JSONPath":
                if (stateElement.TryGetProperty("ItemsPath", out var itemsPathElement))
                {
                    var itemsPath = itemsPathElement.GetString() ?? throw new InvalidOperationException($"'ItemsPath' in Map state ({stateContext.StateName}) must be a string.");
                    (executionContext, var itemsPathResult) = await ExecutePassStateForJsonPath(executionContext, itemsPath, itemReaderResult.Output, stateContext.Variables, cancellationToken);
                    
                    if (itemsPathResult is not StepFunctionStateResult.Success success)
                    {
                        return (executionContext, itemsPathResult);
                    }

                    itemReaderResult = success;
                }
                break;
            case "JSONata":
                if (stateElement.TryGetProperty("Items", out var itemsElement))
                {
                    (executionContext, var itemsResult) = await ExecutePassStateForJsonAta(executionContext, itemsElement, itemReaderResult.Output, stateContext.Variables, cancellationToken);
                    
                    if (itemsResult is not StepFunctionStateResult.Success success)
                    {
                        return (executionContext, itemsResult);
                    }

                    itemReaderResult = success;
                }
                break;
            
            default:
                throw new InvalidOperationException($"Unsupported query language '{stateContext.QueryLanguage}' in Map state ({stateContext.StateName}).");
        }
        
        // selecting items pt2 - 'ItemSelector' can be used to transform each item in array ('Parameters' is the legacy name for this)
        if (!stateElement.TryGetProperty("ItemSelector", out var itemSelectorElement) && !stateElement.TryGetProperty("Parameters", out itemSelectorElement))
        {
            return (executionContext, itemReaderResult);
        }
        
        // we now know enough to setup a mock and use TestState to apply the ItemSelector, this _will_ break if there is an item batcher but that is not currently supported anyway
        using var itemsDocument = JsonDocument.Parse(itemReaderResult.Output);
        var items = itemsDocument.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
        
        (executionContext, var selectorResult) = await TestState(executionContext, stateContext, new MockInput
        {
            Result = "[" + string.Join(",", items.Select(_ => "null")) + "]",
        }, cancellationToken);
        
        if (selectorResult is not StepFunctionStateResult.Success selectorSuccess)
        {
            return (executionContext, selectorResult);
        }
        
        var result = selectorSuccess with { Output = selectorSuccess.InspectionData.AfterItemSelector };
        return (executionContext, result);
    }
        
    private static async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> BatchMapStateItems(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, JsonElement stateElement, StepFunctionStateResult.Success itemsResult)
    {
        if (stateElement.TryGetProperty("ItemBatcher", out _))
        {
            throw new NotImplementedException($"'ItemBatcher' in Map state ({stateContext.StateName}) is not currently supported.");
        }
        
        // default is no batching, each item is processed individually
        return (executionContext, itemsResult);
    }

    private async Task<(StepFunctionExecutionContext, StepFunctionStateResult)> ProcessMapStateItems(StepFunctionExecutionContext executionContext, StepFunctionStateContext stateContext, JsonElement stateElement, string itemsJson, CancellationToken cancellationToken)
    {
        if (!stateElement.TryGetProperty("ItemProcessor", out var itemProcessorElement) && !stateElement.TryGetProperty("Iterator", out itemProcessorElement))
        {
            throw new InvalidOperationException($"Map state ({stateContext.StateName}) must define an 'ItemProcessor' or 'Iterator'.");
        }
        
        if (stateElement.TryGetProperty("ProcessorConfig", out var processorConfigElement) && processorConfigElement.TryGetProperty("Mode", out var modeElement))
        {
            var mode = modeElement.GetString();
            if (mode != "INLINE")
            {
                throw new NotImplementedException($"Map state with 'ProcessorConfig.Mode' set to '{mode}' is not currently supported (only 'INLINE' is supported).");
            }
        }
        
        var itemProcessorStartAt = itemProcessorElement.GetProperty("StartAt").GetString() ?? throw new InvalidOperationException("ItemProcessor definition must contain a StartAt property.");
        var itemProcessorStateContext = stateContext with
        {
            StateName = itemProcessorStartAt,
            QueryLanguage = itemProcessorElement.GetQueryLanguage() ?? stateContext.QueryLanguage
        };

        using var itemsDocument = JsonDocument.Parse(itemsJson);
        
        var results = new List<JsonElement>();
        foreach (var item in itemsDocument.RootElement.EnumerateArray())
        {
            var itemStateContext = itemProcessorStateContext with
            {
                Input = item.GetRawText(),
            };
            
            (executionContext, var itemResult) = await RunToCompletionAsync(executionContext, itemStateContext, cancellationToken);

            switch (itemResult)
            {
                case StepFunctionStateResult.Success success:
                {
                    using var document = JsonDocument.Parse(success.Output);
                    results.Add(document.RootElement.Clone());
                    break;
                }

                default:
                    return (executionContext, itemResult);
            }
        }

        var output = JsonSerializer.Serialize(results);
        var result = new StepFunctionStateResult.Success(null, output, stateContext.Variables, new InspectionData());
        
        return (executionContext, result);
    }

}