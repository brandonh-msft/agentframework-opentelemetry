namespace AgentFramework.OpenTelemetry.Tests;

using System.Diagnostics;

public sealed class ActivitiesTests
{
    [Fact]
    public void FormatTraceParentThenTryParseTraceParentRoundTripsContext()
    {
        ActivityContext original = new(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            ActivityTraceFlags.Recorded,
            traceState: "k=v");

        string traceParent = Activities.FormatTraceParent(original);

        Assert.True(Activities.TryParseTraceParent(traceParent, out ActivityContext parsed));
        Assert.Equal(original.TraceId, parsed.TraceId);
        Assert.Equal(original.SpanId, parsed.SpanId);
        Assert.Equal(original.TraceFlags, parsed.TraceFlags);
    }

    [Fact]
    public void StartActivityWithFallbackParentUsesConversationParentWhenCurrentIsMissing()
    {
        using ActivityCollector collector = new(Activities.AppActivitySource.Name, "traceability-tests.activities");
        using ActivitySource activitySource = new("traceability-tests.activities");
        using Activity? conversationRoot = Activities.AppActivitySource.StartActivity("chat.turn", ActivityKind.Server);

        Assert.NotNull(conversationRoot);

        Activity? previousCurrent = Activity.Current;
        try
        {
            Activity.Current = null;

            using IDisposable scope = Activities.PushConversationParent(conversationRoot);
            using Activity? child = Activities.StartActivityWithFallbackParent(activitySource, "execute_tool lookup");

            Assert.NotNull(child);
            Assert.Equal(conversationRoot.TraceId, child.TraceId);
            Assert.Equal(conversationRoot.SpanId, child.ParentSpanId);
        }
        finally
        {
            Activity.Current = previousCurrent;
        }
    }

    [Fact]
    public void TryGetPreferredParentContextUsesCurrentInvokeActivity()
    {
        using ActivityCollector collector = new(Activities.AppActivitySource.Name);
        using Activity? invokeActivity = Activities.AppActivitySource.StartActivity("invoke_agent", ActivityKind.Internal);

        Assert.NotNull(invokeActivity);
        invokeActivity.SetTag("gen_ai.operation.name", "invoke_agent");

        Assert.True(Activities.TryGetPreferredParentContext(out ActivityContext context));
        Assert.Equal(invokeActivity.TraceId, context.TraceId);
        Assert.Equal(invokeActivity.SpanId, context.SpanId);
    }

    [Fact]
    public void CreateConversationRootContextReturnsValidW3CContext()
    {
        ActivityContext context = Activities.CreateConversationRootContext("dm", "channel/user", "conversation-1");

        Assert.NotEqual(default, context.TraceId);
        Assert.NotEqual(default, context.SpanId);
        Assert.True(Activities.TryParseTraceParent(Activities.FormatTraceParent(context), out ActivityContext parsed));
        Assert.Equal(context.TraceId, parsed.TraceId);
    }
}
