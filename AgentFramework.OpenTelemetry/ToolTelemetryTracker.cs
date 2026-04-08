namespace AgentFramework.OpenTelemetry;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using OpenAI.Responses;

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

internal sealed class ToolTelemetryTracker(ActivitySource activitySource, string agentNameOrId, ActivityContext? parentContext = null) : IDisposable
{
    private readonly Dictionary<string, Activity> _toolActivities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedToolCalls = new(StringComparer.Ordinal);
    private readonly ActivityContext? _parentContext = parentContext;

    public void Observe(AgentResponseUpdate update)
    {
#pragma warning disable MEAI001
        foreach (AIContent content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent functionCall:
                    this.StartToolCall(functionCall.CallId, functionCall.Name, functionCall.Arguments);
                    break;
                case FunctionResultContent functionResult:
                    this.CompleteToolCall(functionResult.CallId, functionResult.Result, functionResult.Exception);
                    break;
                case CodeInterpreterToolCallContent codeCall:
                    this.StartToolCall(codeCall.CallId, "code_interpreter", codeCall.Inputs);
                    break;
                default:
                    this.TryObserveUnknownToolPayload(content.RawRepresentation);
                    break;
            }
        }
#pragma warning restore MEAI001

#pragma warning disable OPENAI001
        if (ReferenceEquals(update.RawRepresentation, update))
        {
            return;
        }

        if (update.AsChatResponseUpdate().RawRepresentation is not StreamingResponseOutputItemDoneUpdate outputItemDone)
        {
            return;
        }

        switch (outputItemDone.Item)
        {
            case FunctionCallResponseItem functionCall:
                this.StartToolCall(functionCall.CallId, functionCall.FunctionName, functionCall.FunctionArguments);
                break;
            case FunctionCallOutputResponseItem functionResult:
                this.CompleteToolCall(functionResult.CallId, functionResult, null);
                break;
            case CodeInterpreterCallResponseItem codeCall:
                this.CompleteToolCall(codeCall.Id, codeCall, null);
                break;
            case ReasoningResponseItem:
                break;
            default:
                this.TryObserveUnknownToolPayload(outputItemDone.Item);
                break;
        }
#pragma warning restore OPENAI001
    }

    public void Dispose()
    {
        foreach ((_, Activity activity) in _toolActivities)
        {
            activity
                .SetTag("gen_ai.tool.call.incomplete", true)
                .Stop();
        }
    }

    private void CompleteToolCall(string? callId, object? result, Exception? exception)
    {
        if (string.IsNullOrWhiteSpace(callId) || _completedToolCalls.Contains(callId))
        {
            return;
        }

        if (!_toolActivities.Remove(callId, out Activity? activity))
        {
            activity = this.StartToolCall(callId, null, null);
            if (activity is null)
            {
                return;
            }
        }

        if (result is not null)
        {
            activity.SetTag("gen_ai.tool.call.result", Serialize(result));
        }

        if (exception is not null)
        {
            activity
                .SetTag("error.type", exception.GetType().FullName)
                .SetStatus(ActivityStatusCode.Error, exception.Message);
        }

        activity.Stop();
        _completedToolCalls.Add(callId);
    }

    private Activity? StartToolCall(string? callId, string? toolName, object? arguments)
    {
        if (string.IsNullOrWhiteSpace(callId) || _completedToolCalls.Contains(callId))
        {
            return null;
        }

        if (_toolActivities.TryGetValue(callId, out Activity? existingActivity))
        {
            if (!string.Equals(existingActivity.GetTagItem("gen_ai.tool.name") as string, toolName, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(toolName)
                && string.Equals(existingActivity.GetTagItem("gen_ai.tool.name") as string, "unknown", StringComparison.Ordinal))
            {
                UpdateToolNameOnActivity(existingActivity, toolName);
            }

            return existingActivity;
        }

        string effectiveToolName = string.IsNullOrWhiteSpace(toolName) ? "unknown" : toolName;
        Activity? activity = _parentContext is { } capturedParent
            && (Activity.Current is null || Activity.Current.TraceId != capturedParent.TraceId)
                ? activitySource.StartActivity($"execute_tool {effectiveToolName}", ActivityKind.Internal, capturedParent)
                : Activities.StartActivityWithFallbackParent(activitySource, $"execute_tool {effectiveToolName}", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.DisplayName = $"execute_tool {effectiveToolName}";
        activity.SetTag("gen_ai.operation.name", "execute_tool");
        activity.SetTag("gen_ai.agent.name", agentNameOrId);
        activity.SetTag("gen_ai.tool.name", effectiveToolName);
        activity.SetTag("gen_ai.tool.call.id", callId);
        activity.SetTag("span_type", "tool");

        if (arguments is not null)
        {
            activity.SetTag("gen_ai.tool.call.arguments", Serialize(arguments));
        }

        _toolActivities.Add(callId, activity);
        return activity;
    }

    private void TryObserveUnknownToolPayload(object? payload)
    {
        if (payload is null)
        {
            return;
        }

        string typeName = payload.GetType().Name;
        if (typeName.StartsWith("Internal", StringComparison.Ordinal))
        {
            return;
        }

        string? callId = GetStringProperty(payload, "CallId", "Id");
        if (string.IsNullOrWhiteSpace(callId))
        {
            return;
        }

        bool looksLikeResult =
            typeName.Contains("Output", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Result", StringComparison.OrdinalIgnoreCase);

        string? toolKind = GetStringProperty(payload, "Type", "Kind", "ItemType");
        string? functionName = GetStringProperty(payload, "FunctionName", "Name", "ActionName", "OperationName");
        string? serviceName = GetStringProperty(payload, "ToolName", "ApiName", "ServiceName", "PluginName");
        object? arguments = GetObjectProperty(payload, "FunctionArguments", "Arguments", "Input", "Inputs", "Code");
        object? result = GetObjectProperty(payload, "Output", "Result", "Code");

        string? toolName = BuildToolName(typeName, toolKind, serviceName, functionName);
        if (looksLikeResult)
        {
            if (_toolActivities.TryGetValue(callId, out Activity? existingActivity) && !string.IsNullOrWhiteSpace(toolName))
            {
                UpdateToolNameOnActivity(existingActivity, toolName);
            }

            this.CompleteToolCall(callId, result ?? payload, null);
            return;
        }

        this.StartToolCall(callId, toolName, arguments);
    }

    private static void UpdateToolNameOnActivity(Activity activity, string toolName)
    {
        activity.DisplayName = $"execute_tool {toolName}";
        activity.SetTag("gen_ai.tool.name", toolName);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0046:Convert to conditional expression", Justification = "Would just keep cascading up to a ridiculous conditional expression")]
    private static string? BuildToolName(string typeName, string? toolKind, string? serviceName, string? functionName)
    {
        if (string.Equals(serviceName, "code_interpreter", StringComparison.OrdinalIgnoreCase))
        {
            return "code_interpreter";
        }

        string? normalizedKind = NormalizeSegment(toolKind);
        string? normalizedService = NormalizeSegment(serviceName);
        string? normalizedFunction = NormalizeSegment(functionName);

        if (string.Equals(normalizedKind, "remote_function_call", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(normalizedService) && !string.IsNullOrWhiteSpace(normalizedFunction))
            {
                return $"remote_openapi:{normalizedService}.{normalizedFunction}";
            }

            if (!string.IsNullOrWhiteSpace(normalizedFunction))
            {
                return $"remote_openapi:{normalizedFunction}";
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedService) && !string.IsNullOrWhiteSpace(normalizedFunction))
        {
            return $"{normalizedService}.{normalizedFunction}";
        }

        if (!string.IsNullOrWhiteSpace(normalizedFunction))
        {
            return normalizedFunction;
        }

        if (!string.IsNullOrWhiteSpace(normalizedService))
        {
            return normalizedService;
        }

        return typeName.Contains("CodeInterpreter", StringComparison.OrdinalIgnoreCase)
            ? "code_interpreter"
            : typeName.Contains("Remote", StringComparison.OrdinalIgnoreCase) ? "remote_openapi" : null;
    }

    private static object? GetObjectProperty(object target, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetIndexParameters().Length == 0)
            {
                object? value = property.GetValue(target);
                if (value is not null)
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? GetStringProperty(object target, params string[] propertyNames)
    {
        object? value = GetObjectProperty(target, propertyNames);
        return value switch
        {
            null => null,
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => value.ToString(),
        };
    }

    private static string? NormalizeSegment(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Serialize(object value) => value switch
    {
        string text => text,
        BinaryData binaryData => binaryData.ToString(),
        _ => JsonSerializer.Serialize(value),
    };
}
