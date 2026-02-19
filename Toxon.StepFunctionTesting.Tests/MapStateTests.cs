using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class MapStateTests : TestBase
{
    [Test, Ignore("This test is currently ignored as the framework does not yet support Map states. Implementation is planned for a future release.")]
    public async Task Happy()
    {
        var mocks = new Dictionary<string, IMockProvider>
        {
            ["Work"] = new MockSequence()
                .ThenReturn("{\"value\": \"alpha-result\"}")
                .ThenReturn("{\"value\": \"beta-result\"}")
        };
        var runner = CreateRunner(Definition);
        
        var result = await runner.RunAsync(@"{""items"": [""alpha"", ""beta""]}", mocks);

        AssertSuccess(result, @"[{""value"":""alpha-result""},{""value"":""beta-result""}]");
    }
    
    // TODO add error & retry cases
    
    private const string Definition =
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
              "Iterator": {
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
                    "States.ALL"
                  ],
                  "ResultPath": "$.MapError",
                  "Next": "Handled"
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