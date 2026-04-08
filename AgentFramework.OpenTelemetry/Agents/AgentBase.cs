namespace AgentFramework.OpenTelemetry.Agents;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

public abstract class AgentBase(AIAgent innerAgent) : AIAgent
{
    private protected readonly ActivitySource ActivitySource = Activities.GetSourceForAgent(innerAgent);

    protected AIAgent InnerAgent { get; } = innerAgent;

    protected override string IdCore => this.InnerAgent.Id;

    public override string? Name => this.InnerAgent.Name;

    protected virtual string AgentNameOrId => this.Name ?? this.Id;

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => this.InnerAgent.CreateSessionAsync(cancellationToken);

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => this.InnerAgent.DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken);

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using Activity? invokeActivity = this.StartInvokeAgentActivity();
        return await this.InnerAgent.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using Activity? invokeActivity = this.StartInvokeAgentActivity();
        using ToolTelemetryTracker toolTelemetry = new(this.ActivitySource, this.AgentNameOrId, invokeActivity?.Context);

        await foreach (AgentResponseUpdate update in this.InnerAgent
            .RunStreamingAsync(messages, session, options, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            toolTelemetry.Observe(update);
            yield return update;
        }
    }

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => this.InnerAgent.SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken);

    private protected Activity? StartInvokeAgentActivity()
    {
        Activity? activity = Activities.StartActivityWithFallbackParent(
            this.ActivitySource,
            $"invoke_agent {this.AgentNameOrId}",
            ActivityKind.Internal);

        if (activity is null)
        {
            return null;
        }

        activity.DisplayName = $"invoke_agent {this.AgentNameOrId}";
        activity.SetTag("gen_ai.operation.name", "invoke_agent");
        activity.SetTag("gen_ai.agent.id", this.Id);
        activity.SetTag("gen_ai.agent.name", this.AgentNameOrId);
        activity.SetTag("span_type", "agent");
        return activity;
    }
}
