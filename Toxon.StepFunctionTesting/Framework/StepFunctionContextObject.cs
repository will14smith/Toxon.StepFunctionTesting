using System.Text;
using System.Text.Json;

namespace Toxon.StepFunctionTesting.Framework;

/// <summary>
/// Represents the values used to build the Step Functions Context Object (<c>$$</c>) that is
/// passed to <c>TestState</c>. The stable per-execution values are captured once when a run
/// starts, then combined with per-state values (state name, retry count, task token) for each
/// state invocation so that references like <c>$$.StateMachine.Name</c> or
/// <c>$$.Execution.StartTime</c> resolve, not just <c>$$.Task.Token</c>.
/// </summary>
internal sealed record StepFunctionContextObject(
    string StateMachineName,
    string StateMachineId,
    string ExecutionName,
    string ExecutionId,
    string ExecutionStartTime,
    string? ExecutionRoleArn,
    string ExecutionInput)
{
    private const string Region = "us-east-1";
    private const string AccountId = "123456789012";
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static StepFunctionContextObject Create(StepFunctionRunnerOptions options, string executionInput)
    {
        var stateMachineName = string.IsNullOrEmpty(options.StateMachineName) ? "StateMachine" : options.StateMachineName;
        var executionName = string.IsNullOrEmpty(options.ExecutionName) ? Guid.NewGuid().ToString() : options.ExecutionName;

        return new StepFunctionContextObject(
            stateMachineName,
            $"arn:aws:states:{Region}:{AccountId}:stateMachine:{stateMachineName}",
            executionName,
            $"arn:aws:states:{Region}:{AccountId}:execution:{stateMachineName}:{executionName}",
            DateTimeOffset.UtcNow.ToString(TimestampFormat),
            options.RoleArn,
            executionInput);
    }

    public string ToJson(string stateName, int retryCount, string? taskToken)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("Execution");
            writer.WriteString("Id", ExecutionId);
            writer.WriteString("Name", ExecutionName);
            writer.WriteString("StartTime", ExecutionStartTime);
            if (ExecutionRoleArn is not null)
            {
                writer.WriteString("RoleArn", ExecutionRoleArn);
            }
            writer.WritePropertyName("Input");
            WriteInput(writer, ExecutionInput);
            writer.WriteEndObject();

            writer.WriteStartObject("StateMachine");
            writer.WriteString("Id", StateMachineId);
            writer.WriteString("Name", StateMachineName);
            writer.WriteEndObject();

            writer.WriteStartObject("State");
            writer.WriteString("Name", stateName);
            writer.WriteString("EnteredTime", DateTimeOffset.UtcNow.ToString(TimestampFormat));
            writer.WriteNumber("RetryCount", retryCount);
            writer.WriteEndObject();

            if (taskToken is not null)
            {
                writer.WriteStartObject("Task");
                writer.WriteString("Token", taskToken);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteInput(Utf8JsonWriter writer, string input)
    {
        try
        {
            using var document = JsonDocument.Parse(input);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
        }
    }
}
