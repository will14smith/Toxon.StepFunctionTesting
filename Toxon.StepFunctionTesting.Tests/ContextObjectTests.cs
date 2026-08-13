using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class ContextObjectTests : TestBase
{
    [Test]
    public async Task ContextObjectResolvesForMockedTask()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"Payload\": {}}")
        };
        var runner = CreateRunner("""
                                  {
                                    "StartAt": "Work",
                                    "QueryLanguage": "JSONPath",
                                    "States": {
                                      "Work": {
                                        "Type": "Task",
                                        "Resource": "arn:aws:states:::lambda:invoke",
                                        "Parameters": {
                                          "FunctionName": "fn",
                                          "Payload": {
                                            "stateMachineName.$": "$$.StateMachine.Name",
                                            "executionName.$": "$$.Execution.Name",
                                            "stateName.$": "$$.State.Name"
                                          }
                                        },
                                        "End": true
                                      }
                                    }
                                  }
                                  """, new StepFunctionRunnerOptions { RequireMocks = true, StateMachineName = "my-machine", ExecutionName = "my-execution" });

        var result = await runner.RunAsync("{}", mocks);

        Assert.That(result.Result, Is.TypeOf<StepFunctionStateResult.Success>(), $"Expected result to be a success.\nActual:\n{result}");
        Assert.That(result.Invocations, Has.One.Matches<StepFunctionStateInvocation>(x
            => x.State.StateName == "Work"
            && AreJsonEqual(@"{""FunctionName"": ""fn"", ""Payload"": {""stateMachineName"": ""my-machine"", ""executionName"": ""my-execution"", ""stateName"": ""Work""}}", x.InspectionData.AfterParameters)));
    }

    [Test]
    public async Task ContextObjectResolvesForWaitForTaskTokenTask()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Callback"] = new MockSequence()
                .ThenReturn("{\"ok\": true}")
        };
        var runner = CreateRunner("""
                                  {
                                    "StartAt": "Callback",
                                    "QueryLanguage": "JSONPath",
                                    "States": {
                                      "Callback": {
                                        "Type": "Task",
                                        "Resource": "arn:aws:states:::lambda:invoke.waitForTaskToken",
                                        "Parameters": {
                                          "FunctionName": "fn",
                                          "Payload": {
                                            "stateMachineName.$": "$$.StateMachine.Name",
                                            "startTime.$": "$$.Execution.StartTime",
                                            "token.$": "$$.Task.Token"
                                          }
                                        },
                                        "End": true
                                      }
                                    }
                                  }
                                  """, new StepFunctionRunnerOptions { RequireMocks = true, StateMachineName = "callback-machine" });

        var result = await runner.RunAsync("{}", mocks);

        Assert.That(result.Result, Is.TypeOf<StepFunctionStateResult.Success>(), $"Expected result to be a success.\nActual:\n{result}");
        var invocation = result.Invocations.Single(x => x.State.StateName == "Callback");
        Assert.That(invocation.InspectionData.AfterParameters, Does.Contain("callback-machine"));
        Assert.That(invocation.InspectionData.AfterParameters, Does.Contain("token"));
    }
}
