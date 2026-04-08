namespace AgentFramework.OpenTelemetry.Tests;

using System.Diagnostics;

public sealed class WorkflowHandoffTelemetryTests
{
    [Fact]
    public void RecordAgentHandoffAddsExpectedEventTags()
    {
        using ActivityCollector collector = new("traceability-tests.handoff");
        using ActivitySource source = new("traceability-tests.handoff");
        using Activity? activity = source.StartActivity("chat.turn", ActivityKind.Server);

        Assert.NotNull(activity);

        WorkflowHandoffTelemetry.RecordAgentHandoff("foundry", "local", "needs grounded tool data", activity);

        ActivityEvent handoffEvent = Assert.Single(activity.Events, static e => e.Name == "gen_ai.workflow.handoff");
        Assert.Contains(handoffEvent.Tags, tag => tag.Key == "gen_ai.agent.handoff.from" && string.Equals(tag.Value?.ToString(), "foundry", StringComparison.Ordinal));
        Assert.Contains(handoffEvent.Tags, tag => tag.Key == "gen_ai.agent.handoff.to" && string.Equals(tag.Value?.ToString(), "local", StringComparison.Ordinal));
        Assert.Contains(handoffEvent.Tags, tag => tag.Key == "gen_ai.agent.handoff.reason" && string.Equals(tag.Value?.ToString(), "needs grounded tool data", StringComparison.Ordinal));
    }
}
