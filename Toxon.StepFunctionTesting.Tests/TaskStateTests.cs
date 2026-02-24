using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class TaskStateTests : TestBase
{
    [Test]
    public async Task MockedLambda()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"Payload\": {\"calculation\": 7}}")
        };
        var runner = CreateRunner("""
                                  {
                                    "StartAt": "Work",
                                    "QueryLanguage": "JSONata",
                                    "States": {
                                      "Work": {
                                        "Type": "Task",
                                        "Resource": "arn:aws:states:::lambda:invoke",
                                        "Arguments": {
                                          "FunctionName": "addNumbers",
                                          "Payload": {
                                            "number1": 5,
                                            "number2": "{% $states.input.value %}"
                                          }
                                        },
                                        "Output": {
                                          "result": "{% $states.result.Payload.calculation %}"
                                        },
                                        "End": true
                                      }
                                    }
                                  }
                                  """);
        
        var result = await runner.RunAsync(@"{""value"": 2}", mocks);
        
        AssertSuccess(result, @"{""result"": 7}");
        Assert.That(result.Invocations, Has.One.Matches<StepFunctionStateInvocation>(x 
            => x.State.StateName == "Work" 
            && AreJsonEqual(@"{""FunctionName"": ""addNumbers"", ""Payload"": {""number1"": 5, ""number2"": 2}}", x.InspectionData.AfterArguments)
            && AreJsonEqual(@"{""Payload"": {""calculation"": 7}}", x.InspectionData.Result))
        );
    }
}