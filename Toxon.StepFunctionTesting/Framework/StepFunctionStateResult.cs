using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public abstract record StepFunctionStateResult
{
    public record Success(string? NextStateName, string Output, string Variables, InspectionData InspectionData) : StepFunctionStateResult;
    public record Failed(string Error, string Cause) : StepFunctionStateResult;
    public record CaughtError(string NextStateName, string Output, string Variables) : StepFunctionStateResult;
    public record Retriable : StepFunctionStateResult;

    public static StepFunctionStateResult FromResponse(TestStateResponse response)
    {
        if (response.Status == TestExecutionStatus.SUCCEEDED)
        {
            return new Success(
                NextStateName: response.NextState,
                Output: response.Output,
                Variables: response.InspectionData.Variables,
                InspectionData: response.InspectionData
            );
        }

        if (response.Status == TestExecutionStatus.FAILED)
        {
            return new Failed(
                Error: response.Error ?? string.Empty,
                Cause: response.Cause ?? string.Empty
            );
        }

        if (response.Status == TestExecutionStatus.CAUGHT_ERROR)
        {
            return new CaughtError(
                NextStateName: response.NextState,
                Output: response.Output,
                Variables: response.InspectionData.Variables
            );
        }

        if (response.Status == TestExecutionStatus.RETRIABLE)
        {
            return new Retriable();
        }
        
        throw new InvalidOperationException($"Unsupported TestStateResponse status: {response.Status}");
    }
}