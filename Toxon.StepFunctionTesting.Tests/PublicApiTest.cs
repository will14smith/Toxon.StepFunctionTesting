using PublicApiGenerator;
using Toxon.StepFunctionTesting.Framework;

namespace Toxon.StepFunctionTesting.Tests;

public class PublicApiTest
{
    [Test]
    public Task PublicApiHasNotChanged()
    {
        var assembly = typeof(StepFunctionRunner).Assembly;
        var publicApi = assembly.GeneratePublicApi();
        return Verify(publicApi);
    }
}