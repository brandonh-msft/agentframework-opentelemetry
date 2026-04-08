namespace AgentFramework.OpenTelemetry.Tests;

using System.Diagnostics;

public sealed class PipelinePolicyHelpersTests
{
    [Fact]
    public void ApplyTraceContextSetsTraceParentAndClearsTraceStateWhenEmpty()
    {
        using ActivityCollector collector = new(Activities.AppActivitySource.Name);
        using Activity? current = Activities.AppActivitySource.StartActivity("chat.turn", ActivityKind.Server);

        Assert.NotNull(current);
        current.SetTag("gen_ai.operation.name", "chat_turn");

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["tracestate"] = "stale",
        };

        PipelinePolicyHelpers.ApplyTraceContext(
            (name, value) => headers[name] = value,
            name => headers.Remove(name));

        Assert.True(headers.TryGetValue("traceparent", out string? traceParent));
        Assert.True(Activities.TryParseTraceParent(traceParent, out ActivityContext parsed));
        Assert.Equal(current.TraceId, parsed.TraceId);
        Assert.False(headers.ContainsKey("tracestate"));
    }

    [Theory]
    [InlineData("POST", "https://ai.azure.com/api", true)]
    [InlineData("POST", "https://foo.ai.azure.com/api", true)]
    [InlineData("GET", "https://foo.ai.azure.com/api", false)]
    [InlineData("POST", "https://example.com/api", false)]
    public void IsAzureAIPostRequestReturnsExpectedResult(string method, string url, bool expected)
        => Assert.Equal(expected, PipelinePolicyHelpers.IsAzureAIPostRequest(method, new Uri(url)));

    [Fact]
    public void LogAzureAIRequestBodyRecordsRequestEventAndReplacesContent()
    {
        using ActivityCollector collector = new("traceability-tests.pipeline");
        using ActivitySource source = new("traceability-tests.pipeline");
        using Activity? activity = source.StartActivity("request", ActivityKind.Client);

        Assert.NotNull(activity);

        FakeDisposableContent content = new("{\"query\":\"test\"}");
        bool replacedContent = false;

        PipelinePolicyHelpers.LogAzureAIRequestBody(
            method: "POST",
            uri: new Uri("https://foo.ai.azure.com/openai/v1/responses"),
            content: content,
            readContent: static c => BinaryData.FromString(c.Payload),
            setContent: _ => replacedContent = true);

        ActivityEvent requestEvent = Assert.Single(activity.Events, static e => e.Name == "azure.ai.request");
        Assert.Contains(requestEvent.Tags, tag => tag.Key == "azure.ai.request.body" && (tag.Value?.ToString() ?? string.Empty).Contains("\"query\":\"test\"", StringComparison.Ordinal));
        Assert.True(content.IsDisposed);
        Assert.True(replacedContent);
    }

    [Fact]
    public void LogAzureAIRequestBodyRecordsErrorEventWhenReadFails()
    {
        using ActivityCollector collector = new("traceability-tests.pipeline.errors");
        using ActivitySource source = new("traceability-tests.pipeline.errors");
        using Activity? activity = source.StartActivity("request", ActivityKind.Client);

        Assert.NotNull(activity);

        FakeDisposableContent content = new("{\"query\":\"test\"}");

        PipelinePolicyHelpers.LogAzureAIRequestBody(
            method: "POST",
            uri: new Uri("https://foo.ai.azure.com/openai/v1/responses"),
            content: content,
            readContent: static _ => throw new InvalidOperationException("boom"),
            setContent: static _ => { });

        ActivityEvent errorEvent = Assert.Single(activity.Events, static e => e.Name == "azure.ai.request.body.log_error");
        Assert.Contains(errorEvent.Tags, tag => tag.Key == "exception.type" && string.Equals(tag.Value?.ToString(), nameof(InvalidOperationException), StringComparison.Ordinal));
    }

    private sealed class FakeDisposableContent(string payload) : IDisposable
    {
        public string Payload { get; } = payload;

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
