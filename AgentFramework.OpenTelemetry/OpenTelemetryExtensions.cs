namespace AgentFramework.OpenTelemetry;

using global::OpenTelemetry.Resources;
using global::OpenTelemetry.Trace;

public static class OpenTelemetryExtensions
{
    public static void EnableAzureExperimentalTracing(bool includeGenAiMessageContent = true)
    {
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
        AppContext.SetSwitch("Azure.Experimental.TraceGenAIMessageContent", includeGenAiMessageContent);
    }

    public static TracerProviderBuilder AddAgentFrameworkOpenTelemetry(
        this TracerProviderBuilder builder,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        builder
            .AddSource("Azure.AI.Agents.Persistent.*")
            .AddSource(Activities.AppActivitySource.Name)
            .AddSource(Activities.AgentActivitySourceName)
            .AddSource("Azure.AI.Projects.*")
            .AddSource("OpenAI.*")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));

        return builder;
    }
}
