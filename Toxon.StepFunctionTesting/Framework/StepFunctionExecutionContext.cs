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

    public JsonElement GetStateElement(string stateName)
    {
        if (!RootElement.TryGetProperty("States", out var statesElement))
        {
            throw new InvalidOperationException("The definition does not contain a 'States' property.");
        }
        
        if (!statesElement.TryGetProperty(stateName, out var stateElement))
        {
            throw new InvalidOperationException($"State '{stateName}' was not found in the definition.");
        }

        return stateElement;
    }
    public StepFunctionExecutionContext AsBranch(JsonElement branch) => this with { RootElement = branch };
    public StepFunctionExecutionContext UpdateFromBranch(StepFunctionExecutionContext branch) => this with { Attempts = branch.Attempts };
}