using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class MapStateTests : TestBase
{
    [Test]
    public async Task Mocked()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["ProcessItems"] = new MockSequence()
                .ThenReturn("[{\"value\":\"alpha-result\"},{\"value\":\"beta-result\"}]")
        };
        var runner = CreateRunner(DefinitionJsonPath);
        
        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);

        AssertSuccess(result, @"[{""value"":""alpha-result""},{""value"":""beta-result""}]");
    }
    
    [Test]
    public async Task HappyJsonPath()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenReturn("{\"value\": \"beta-result\"}")
        };
        var runner = CreateRunner(DefinitionJsonPath);
        
        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);

        AssertSuccess(result, @"[{""value"":""alpha-result""},{""value"":""beta-result""}]");
    }
    
    [Test]
    public async Task HappyJsonAta()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenReturn("{\"value\": \"beta-result\"}")
        };
        var runner = CreateRunner(DefinitionJsonAta);
        
        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);

        AssertSuccess(result, @"[{""value"":""alpha-result""},{""value"":""beta-result""}]");
    }

    
    [Test]
    public async Task Retry()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenFail("RetryableError")
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenReturn("{\"value\": \"beta-result\"}")
        };
        var runner = CreateRunner(DefinitionJsonPath);
        
        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);

        AssertSuccess(result, @"[{""value"":""alpha-result""},{""value"":""beta-result""}]");
    }
    
    [Test]
    public async Task CaughtError()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenFail("CatchableError")
        };
        var runner = CreateRunner(DefinitionJsonPath);
        
        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);

        AssertSuccess(result, @"{""items"": [""alpha"", ""beta""], ""MapError"": {""Error"": ""CatchableError"", ""Cause"": ""CatchableError""}, ""Handled"": {""handled"": true}}");
    }

    [Test]
    public async Task UnhandledError()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenFail("UnhandledError", "this error is not caught and should cause the whole execution to fail")
        };
        var runner = CreateRunner(DefinitionJsonPath);

        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);
        
        AssertFailed(result, "UnhandledError", "this error is not caught and should cause the whole execution to fail");
    }

    private const string DefinitionJsonPath =
        """
        {
          "Comment": "Map state test for iterator execution and error handling.",
          "StartAt": "ProcessItems",
          "States": {
            "ProcessItems": {
              "Type": "Map",
              "ItemsPath": "$.items",
              "ItemSelector": {
                "value.$": "$$.Map.Item.Value"
              },
              "ItemProcessor": {
                "StartAt": "Work",
                "States": {
                  "Work": {
                    "Type": "Task",
                    "Resource": "arn:aws:states:::lambda:invoke",
                    "Parameters": {
                      "FunctionName": "ProcessItemFunction",
                      "Payload": {
                        "value.$": "$.value"
                      }
                    },
                    "End": true
                  }
                }
              },
              "Catch": [
                {
                  "ErrorEquals": [
                    "CatchableError"
                  ],
                  "ResultPath": "$.MapError",
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
    private const string DefinitionJsonAta =
        """
        {
          "Comment": "Map state test for iterator execution and error handling.",
          "StartAt": "ProcessItems",
          "QueryLanguage": "JSONata",
          "States": {
            "ProcessItems": {
              "Type": "Map",
              "ItemSelector": {
                "value": "{% $states.context.Map.Item.Value %}"
              },
              "ItemProcessor": {
                "StartAt": "Work",
                "States": {
                  "Work": {
                    "Type": "Task",
                    "Resource": "arn:aws:states:::lambda:invoke",
                    "End": true,
                    "Arguments": {
                      "FunctionName": "ProcessItemFunction",
                      "Payload": {
                        "value": "{% $states.input.value %}"
                      }
                    }
                  }
                }
              },
              "Catch": [
                {
                  "ErrorEquals": [
                    "CatchableError"
                  ],
                  "Next": "Handled",
                  "Output": "{% $merge($states.input, { \"MapError\": $states.errorOutput }) %}"
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
              "Next": "AllDone",
              "Items": "{% $states.input.items %}"
            },
            "Handled": {
              "Type": "Pass",
              "Next": "AllDone",
              "Output": "{% $merge($states.input, { \"Handled\": { \"handled\": true } }) %}"
            },
            "AllDone": {
              "Type": "Succeed"
            }
          }
        }
        """;
}