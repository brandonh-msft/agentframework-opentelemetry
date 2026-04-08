namespace AgentFramework.OpenTelemetry.Tests;

using global::OpenTelemetry;
using global::OpenTelemetry.Trace;

public sealed class OpenTelemetryExtensionsTests
{
    [Fact]
    public void EnableAzureExperimentalTracingConfiguresExpectedSwitches()
    {
        OpenTelemetryExtensions.EnableAzureExperimentalTracing(includeGenAiMessageContent: false);

        Assert.True(AppContext.TryGetSwitch("Azure.Experimental.EnableActivitySource", out bool activitySourceEnabled));
        Assert.True(activitySourceEnabled);
        Assert.True(AppContext.TryGetSwitch("Azure.Experimental.TraceGenAIMessageContent", out bool includeMessageContent));
        Assert.False(includeMessageContent);
    }

    [Fact]
    public void AddAgentFrameworkOpenTelemetryThrowsForBlankServiceName()
    {
        TracerProviderBuilder builder = Sdk.CreateTracerProviderBuilder();

        Assert.Throws<ArgumentException>(() => builder.AddAgentFrameworkOpenTelemetry(" "));
    }
}
