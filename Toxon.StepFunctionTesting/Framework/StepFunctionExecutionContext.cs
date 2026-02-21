using System.Collections.Immutable;
using System.Text.Json;

namespace Toxon.StepFunctionTesting.Framework;

public record StepFunctionExecutionContext(
    JsonElement RootElement,
    IReadOnlyDictionary<string, IMockProvider> Mocks,
    ImmutableDictionary<string, int> Attempts
)
{
    public string? QueryLanguage => RootElement.GetQueryLanguage();

    public JsonElement GetStateElement(string stateName) => GetStateElement(RootElement, stateName) 
        ?? throw new InvalidOperationException($"State '{stateName}' was not found in the definition.");
    
    private static JsonElement? GetStateElement(JsonElement element, string stateName)
    {
        if (!element.TryGetProperty("States", out var statesElement))
        {
            return null;
        }

        if (statesElement.TryGetProperty(stateName, out var stateElement))
        {
            return stateElement;
        }
        
        // recursively search nested states
        foreach (var state in statesElement.EnumerateObject())
        {
            var stateType = state.Value.GetProperty("Type").GetString();
            switch (stateType)
            {
                case "Parallel":
                {
                    var branchesElement = state.Value.GetProperty("Branches");
                    foreach (var branch in branchesElement.EnumerateArray())
                    {
                        var result = GetStateElement(branch, stateName);
                        if (result != null)
                        {
                            return result;
                        }
                    }

                    break;
                }

                case "Map":
                {
                    if(!state.Value.TryGetProperty("ItemProcessor", out var itemProcessor) && !state.Value.TryGetProperty("Iterator", out itemProcessor))
                    {
                        continue;
                    }
                
                    var result = GetStateElement(itemProcessor, stateName);
                    if (result != null)
                    {
                        return result;
                    }

                    break;
                }
            }
        }

        return null;
    }
}