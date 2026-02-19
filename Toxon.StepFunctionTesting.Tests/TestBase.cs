using System.Text.Json;
using System.Text.Json.Nodes;
using Amazon.StepFunctions;
using Json.Patch;
using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public abstract class TestBase
{
    
    private readonly static JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

    protected StepFunctionRunner CreateRunner(string definition) => new(
        new AmazonStepFunctionsClient(),
        definition,
        new StepFunctionRunnerOptions { RequireMocks = true }
    );
    
    protected static void AssertSuccess(StepFunctionStateResult result, string expectedOutput)
    {
        Assert.That(result, Is.TypeOf<StepFunctionStateResult.Success>(), $"Expected result to be a success.\nActual:\n{result}");
        var successResult = (StepFunctionStateResult.Success)result;

        var expectedJson = JsonNode.Parse(expectedOutput);
        var actualJson = JsonNode.Parse(successResult.Output);

        var diff = actualJson.CreatePatch(expectedJson);
        Assert.That(diff.Operations, Is.Empty, $"Expected and actual JSON do not match.\nExpected:\n{expectedJson}\nActual:\n{actualJson}\nDifferences:\n{JsonSerializer.Serialize(diff, JsonSerializerOptions)}");
    }
    
    protected static void AssertFailed(StepFunctionStateResult result, string? expectedError = null, string? expectedCause = null)
    {
        Assert.That(result, Is.TypeOf<StepFunctionStateResult.Failed>());
        var failedResult = (StepFunctionStateResult.Failed)result;

        if (expectedError is not null)
        {
            Assert.That(failedResult.Error, Is.EqualTo(expectedError));
        }

        if (expectedCause is not null)
        {
            Assert.That(failedResult.Cause, Is.EqualTo(expectedCause));
        }
    }
}