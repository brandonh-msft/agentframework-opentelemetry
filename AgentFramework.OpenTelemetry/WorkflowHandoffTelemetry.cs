namespace AgentFramework.OpenTelemetry;

using System.Diagnostics;

public static class WorkflowHandoffTelemetry
{
    public static void RecordAgentHandoff(string fromAgent, string toAgent, string? reason = null, Activity? activity = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromAgent);
        ArgumentException.ThrowIfNullOrWhiteSpace(toAgent);

        Activity? effectiveActivity = activity ?? Activity.Current;
        if (effectiveActivity is null)
        {
            return;
        }

        ActivityTagsCollection tags =
        [
            new("gen_ai.agent.handoff.from", fromAgent),
            new("gen_ai.agent.handoff.to", toAgent),
        ];

        if (!string.IsNullOrWhiteSpace(reason))
        {
            tags.Add("gen_ai.agent.handoff.reason", reason);
        }

        effectiveActivity.AddEvent(new ActivityEvent("gen_ai.workflow.handoff", tags: tags));
    }
}
