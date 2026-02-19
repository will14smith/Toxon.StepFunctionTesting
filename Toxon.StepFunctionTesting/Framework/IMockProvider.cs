using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public interface IMockProvider
{
    MockInput GetMock(int attempt);
}
