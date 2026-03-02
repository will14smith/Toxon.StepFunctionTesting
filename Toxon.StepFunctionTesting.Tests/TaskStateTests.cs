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

    [Test]
    public async Task ExecutionInputMaintainedAcrossTaskStates()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["FirstTask"] = new MockSequence()
                .ThenReturn("{\"result\": \"processed\"}"),
            ["SecondTask"] = new MockSequence()
                .ThenReturn("{\"final\": \"done\"}")
        };
        var runner = CreateRunner("""
                                  {
                                    "StartAt": "FirstTask",
                                    "QueryLanguage": "JSONata",
                                    "States": {
                                      "FirstTask": {
                                        "Type": "Task",
                                        "Resource": "arn:aws:states:::lambda:invoke",
                                        "Arguments": {
                                          "FunctionName": "processData",
                                          "Payload": "{% $states.input %}"
                                        },
                                        "Next": "CaptureOriginalInput"
                                      },
                                      "CaptureOriginalInput": {
                                        "Type": "Pass",
                                        "Next": "SecondTask",
                                        "Assign": {
                                          "originalValue": "{% $states.context.Execution.Input.originalData %}",
                                          "taskResult": "{% $states.input.result %}"
                                        }
                                      },
                                      "SecondTask": {
                                        "Type": "Task",
                                        "Resource": "arn:aws:states:::lambda:invoke",
                                        "Arguments": {
                                          "FunctionName": "finalProcess",
                                          "Payload": "{% $states.input %}"
                                        },
                                        "End": true
                                      }
                                    }
                                  }
                                  """);

        var result = await runner.RunAsync(@"{""originalData"": ""test-value-123""}", mocks);

        Assert.That(result.Result, Is.TypeOf<StepFunctionStateResult.Success>());
        Assert.That(result.Invocations, Has.Count.EqualTo(3));
        var captureState = result.Invocations.First(x => x.State.StateName == "CaptureOriginalInput");
        var captureOutput = captureState.Result as StepFunctionStateResult.Success;
        Assert.That(captureOutput, Is.Not.Null);
        Assert.That(captureOutput!.Output, Does.Contain("test-value-123"),
            "The Pass state should be able to access the original execution input via $states.context.Execution.Input after a Task state");
    }
}