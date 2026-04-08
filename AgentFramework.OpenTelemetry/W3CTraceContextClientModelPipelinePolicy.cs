namespace AgentFramework.OpenTelemetry;

using System.ClientModel.Primitives;

using static AgentFramework.OpenTelemetry.PipelinePolicyHelpers;

public sealed class W3CTraceContextClientModelPipelinePolicy : PipelinePolicy
{
    public static W3CTraceContextClientModelPipelinePolicy Instance { get; } = new();

    private W3CTraceContextClientModelPipelinePolicy()
    {
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ThisApplyTraceContext(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        ThisApplyTraceContext(message);
        ThisLogAzureAIRequestBody(message);
        await ProcessNextAsync(message, pipeline, currentIndex).ConfigureAwait(false);
    }

    private static void ThisLogAzureAIRequestBody(PipelineMessage message)
    {
        LogAzureAIRequestBody(
            message.Request.Method,
            message.Request.Uri,
            message.Request.Content,
            readContent: static content => CaptureRequestBody(buffer => content.WriteTo(buffer)),
            setContent: requestBody => message.Request.Content = System.ClientModel.BinaryContent.Create(requestBody));
    }

    private static void ThisApplyTraceContext(PipelineMessage message)
        => ApplyTraceContext(message.Request.Headers.Set, headerName => message.Request.Headers.Remove(headerName));
}
