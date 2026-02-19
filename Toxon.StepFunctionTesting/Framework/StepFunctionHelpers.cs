using System.Text.Json;

namespace Toxon.StepFunctionTesting.Framework;

public static class StepFunctionHelpers
{
    public static string? GetQueryLanguage(this JsonElement element) => element.TryGetProperty("QueryLanguage", out var queryLanguage) ? queryLanguage.GetString() : null;

    public static string? GetStateType(this JsonElement element) => element.TryGetProperty("Type", out var type) ? type.GetString() : null;

    public static string? GetTaskResource(this JsonElement element) => element.TryGetProperty("Resource", out var resource) ? resource.GetString() : null;

    public static string? GetNextStateName(this JsonElement element)
    {
        if (element.TryGetProperty("Next", out var nextState))
        {
            return nextState.GetString();
        }

        if (element.TryGetProperty("End", out var endState) && endState.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        throw new InvalidOperationException("State must define Next or End.");
    }

    public static bool IsTaskState(this JsonElement element) => element.GetStateType() == "Task";

    public static bool IsParallelState(this JsonElement element) => element.GetStateType() == "Parallel";

    public static bool IsMapState(this JsonElement element) => element.GetStateType() == "Map";

    public static bool IsWaitState(this JsonElement element) => element.GetStateType() == "Wait";

    public static bool RequiresTaskToken(this JsonElement element) => element.IsTaskState() && element.GetTaskResource()?.Contains(".waitForTaskToken") == true;
}