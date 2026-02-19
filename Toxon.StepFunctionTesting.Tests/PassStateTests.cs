using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class PassStateTests : TestBase
{
    [Test]
    public async Task Output()
    {
        var mocks = new Dictionary<string, IMockProvider>();
        var runner = CreateRunner("""
                                  {
                                    "StartAt": "Output",
                                    "QueryLanguage": "JSONata",
                                    "States": {
                                      "Output": {
                                        "Type": "Pass",
                                        "Output": {
                                          "value": 15
                                        },
                                        "End": true
                                      }
                                    }
                                  }
                                  """);
        
        var result = await runner.RunAsync(@"{""input"": true}", mocks);

        AssertSuccess(result, @"{""value"": 15}");
    }
    
    [Test]
    public async Task AssignThenOutput()
    {
        var mocks = new Dictionary<string, IMockProvider>();
        var runner = CreateRunner("""
                                  {
                                    "StartAt": "Assign",
                                    "QueryLanguage": "JSONata",
                                    "States": {
                                      "Assign": {
                                        "Type": "Pass",
                                        "Assign": {
                                          "a": 5
                                        },
                                        "Next": "Work"
                                      },
                                      "Work": {
                                        "Type": "Pass",
                                        "Output": {
                                          "value": "{% $a + 10 %}"
                                        },
                                        "End": true
                                      }
                                    }
                                  }
                                  """);
        
        var result = await runner.RunAsync(@"{""input"": true}", mocks);

        AssertSuccess(result, @"{""value"": 15}");
    }
}