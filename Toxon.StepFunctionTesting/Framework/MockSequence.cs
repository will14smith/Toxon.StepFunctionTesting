using Amazon.StepFunctions;
using Amazon.StepFunctions.Model;

namespace Toxon.StepFunctionTesting.Framework;

public sealed class MockSequence(MockSequenceTailMode tailMode = MockSequenceTailMode.RepeatLast) : IMockProvider
{
    private readonly List<MockInput> _steps = [];

    public MockSequence ThenReturn(string jsonResult, MockResponseValidationMode? validationMode = null)
    {
        if (string.IsNullOrWhiteSpace(jsonResult))
        {
            throw new ArgumentException("Mock result JSON cannot be empty.", nameof(jsonResult));
        }

        _steps.Add(new MockInput
        {
            Result = jsonResult,
            FieldValidationMode = validationMode ?? MockResponseValidationMode.NONE
        });

        return this;
    }

    public MockSequence ThenFail(string error, string? cause = null)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Mock error code cannot be empty.", nameof(error));
        }

        _steps.Add(new MockInput
        {
            ErrorOutput = new MockErrorOutput
            {
                Error = error,
                Cause = cause ?? error
            }
        });

        return this;
    }

    public MockInput GetMock(int index)
    {
        if (index >= _steps.Count)
        {
            return tailMode switch
            {
                MockSequenceTailMode.RepeatLast => _steps.Count > 0 ? _steps[^1] : throw new InvalidOperationException("Mock sequence is empty."),
                MockSequenceTailMode.ThrowException => throw new InvalidOperationException("Mock sequence is empty."),
                
                _ => throw new InvalidOperationException($"Unsupported tail mode: {tailMode}")
            };
        }

        return _steps[index];
    }
}

public enum MockSequenceTailMode
{
    RepeatLast,
    ThrowException
}
