namespace AgentFramework.OpenTelemetry;

using System.Diagnostics;
using System.IO;

internal static class PipelinePolicyHelpers
{
    private const string TraceParentHeaderName = "traceparent";
    private const string TraceStateHeaderName = "tracestate";

    public static void ApplyTraceContext(Action<string, string> setHeader, Action<string> removeHeader)
    {
        ArgumentNullException.ThrowIfNull(setHeader);
        ArgumentNullException.ThrowIfNull(removeHeader);

        if (!Activities.TryGetPreferredParentContext(out ActivityContext context))
        {
            return;
        }

        string traceParent = Activities.FormatTraceParent(context);
        setHeader(TraceParentHeaderName, traceParent);

        if (string.IsNullOrWhiteSpace(context.TraceState))
        {
            removeHeader(TraceStateHeaderName);
        }
        else
        {
            setHeader(TraceStateHeaderName, context.TraceState);
        }
    }

    public static BinaryData CaptureRequestBody(Action<Stream> writeContent)
    {
        ArgumentNullException.ThrowIfNull(writeContent);

        using var buffer = new MemoryStream();
        writeContent(buffer);
        return BinaryData.FromBytes(buffer.ToArray());
    }

    public static bool IsAzureAIPostRequest(string method, Uri? uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) || uri is null)
        {
            return false;
        }

        string host = uri.Host;
        return string.Equals(host, "ai.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".ai.azure.com", StringComparison.OrdinalIgnoreCase);
    }

    public static void LogAzureAIRequestBody<TContent>(
        string method,
        Uri? uri,
        TContent? content,
        Func<TContent, BinaryData> readContent,
        Action<BinaryData> setContent)
        where TContent : class, IDisposable
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(readContent);
        ArgumentNullException.ThrowIfNull(setContent);

        Activity? activity = Activity.Current;
        if (activity is null || content is null || !IsAzureAIPostRequest(method, uri))
        {
            return;
        }

        try
        {
            BinaryData requestBody = readContent(content);
            setContent(requestBody);
            content.Dispose();

            string requestBodyText = requestBody.ToString();
            if (string.IsNullOrWhiteSpace(requestBodyText))
            {
                return;
            }

            activity.AddEvent(
                new ActivityEvent(
                    "azure.ai.request",
                    tags: new ActivityTagsCollection
                    {
                        { "azure.ai.request.body", requestBodyText },
                    }));
        }
        catch (Exception ex)
        {
            activity.AddEvent(
                new ActivityEvent(
                    "azure.ai.request.body.log_error",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().Name },
                        { "exception.message", ex.Message },
                    }));
        }
    }
}
