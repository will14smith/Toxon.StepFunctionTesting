using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class ParallelStateTests : TestBase
{
    [Test]
    public async Task Happy()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["FetchA"] = new MockSequence()
                .ThenReturn("{\"value\": \"a-result\"}"),
            ["FetchB"] = new MockSequence()
                .ThenReturn("{\"value\": \"b-result\"}")
        };
        var runner = CreateRunner(Definition);
        
        var result = await runner.RunAsync("{}", mocks);

        AssertSuccess(result, @"[{""value"":""a-result""},{""value"":""b-result""}]");
    }
    
    [Test]
    public async Task Retry()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["FetchA"] = new MockSequence()
                .ThenReturn("{\"value\": \"a-result\"}"),
            ["FetchB"] = new MockSequence()
                .ThenFail("RetryableError")
                .ThenReturn("{\"value\": \"b-result\"}")
        };
        var runner = CreateRunner(Definition);
        
        var result = await runner.RunAsync("{}", mocks);

        AssertSuccess(result, @"[{""value"":""a-result""},{""value"":""b-result""}]");
    }
    
    [Test]
    public async Task CaughtError()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["FetchA"] = new MockSequence()
                .ThenReturn("{\"value\": \"a-result\"}"),
            ["FetchB"] = new MockSequence()
                .ThenFail("CatchableError", "this error is caught and handled")
        };
        var runner = CreateRunner(Definition);
        
        var result = await runner.RunAsync("{}", mocks);

        AssertSuccess(result, @"{""Handled"":{""handled"":true},""ParallelError"":{""Error"":""CatchableError"",""Cause"":""this error is caught and handled""}}");
    }
    
    [Test]
    public async Task UnhandledError()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["FetchA"] = new MockSequence()
                .ThenReturn("{\"value\": \"a-result\"}"),
            ["FetchB"] = new MockSequence()
                .ThenFail("UnhandledError")
        };
        var runner = CreateRunner(Definition);
        
        var result = await runner.RunAsync("{}", mocks);

        AssertFailed(result, "UnhandledError");
    }

    private const string Definition =
        """
        {
          "Comment": "Parallel state test for branch outputs and error propagation.",
          "StartAt": "ParallelWork",
          "States": {
            "ParallelWork": {
              "Type": "Parallel",
              "Branches": [
                {
                  "StartAt": "FetchA",
                  "States": {
                    "FetchA": {
                      "Type": "Task",
                      "Resource": "arn:aws:states:::lambda:invoke",
                      "Parameters": {
                        "FunctionName": "FetchAFunction",
                        "Payload": {
                          "Input.$": "$"
                        }
                      },
                      "End": true
                    }
                  }
                },
                {
                  "StartAt": "FetchB",
                  "States": {
                    "FetchB": {
                      "Type": "Task",
                      "Resource": "arn:aws:states:::lambda:invoke",
                      "Parameters": {
                        "FunctionName": "FetchBFunction",
                        "Payload": {
                          "Input.$": "$"
                        }
                      },
                      "End": true
                    }
                  }
                }
              ],
              "Catch": [
                {
                  "ErrorEquals": [
                    "CatchableError"
                  ],
                  "ResultPath": "$.ParallelError",
                  "Next": "Handled"
                }
              ],
              "Retry": [
                {
                  "ErrorEquals": [
                    "RetryableError"
                  ],
                  "IntervalSeconds": 1,
                  "MaxAttempts": 2,
                  "BackoffRate": 2
                }
              ],
              "Next": "AllDone"
            },
            "Handled": {
              "Type": "Pass",
              "Result": {
                "handled": true
              },
              "ResultPath": "$.Handled",
              "Next": "AllDone"
            },
            "AllDone": {
              "Type": "Succeed"
            }
          }
        }
        """;
}