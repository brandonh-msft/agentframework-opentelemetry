namespace AgentFramework.OpenTelemetry;

using Azure.Core;
using Azure.Core.Pipeline;

using static AgentFramework.OpenTelemetry.PipelinePolicyHelpers;

public sealed class W3CTraceContextAzureHttpPipelinePolicy : HttpPipelinePolicy
{
    public static W3CTraceContextAzureHttpPipelinePolicy Instance { get; } = new();

    private W3CTraceContextAzureHttpPipelinePolicy()
    {
    }

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        ThisApplyTraceContext(message);
        ThisLogAzureAIRequestBody(message);
        await ProcessNextAsync(message, pipeline).ConfigureAwait(false);
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        ThisApplyTraceContext(message);
        ProcessNext(message, pipeline);
    }

    private static void ThisLogAzureAIRequestBody(HttpMessage message)
    {
        LogAzureAIRequestBody(
            message.Request.Method.ToString(),
            message.Request.Uri.ToUri(),
            message.Request.Content,
            readContent: content => CaptureRequestBody(buffer => content.WriteTo(buffer, message.CancellationToken)),
            setContent: requestBody => message.Request.Content = RequestContent.Create(requestBody));
    }

    private static void ThisApplyTraceContext(HttpMessage message)
        => ApplyTraceContext(message.Request.Headers.SetValue, headerName => message.Request.Headers.Remove(headerName));
}
