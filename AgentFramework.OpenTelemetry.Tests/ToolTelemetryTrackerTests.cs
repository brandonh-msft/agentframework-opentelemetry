namespace AgentFramework.OpenTelemetry.Tests;

using Microsoft.Extensions.AI;

using System.Diagnostics;

public sealed class ToolTelemetryTrackerTests
{
    [Fact]
    public void ObserveFunctionCallAndResultEmitsCompletedToolActivity()
    {
        using ActivityCollector collector = new("traceability-tests.tools");
        using ActivitySource source = new("traceability-tests.tools");
        using Activity? parent = source.StartActivity("invoke_agent", ActivityKind.Internal);

        Assert.NotNull(parent);
        parent.SetTag("gen_ai.operation.name", "invoke_agent");

        using ToolTelemetryTracker tracker = new(source, "foundry-agent", parent.Context);

        FunctionCallContent functionCall = new(
            callId: "call-1",
            name: "team_lookup",
            arguments: new Dictionary<string, object?>
            {
                ["team"] = 2046,
            });
        tracker.Observe(new AgentResponseUpdate(ChatRole.Assistant, [functionCall]));

        FunctionResultContent functionResult = new(callId: "call-1", result: "ok");
        tracker.Observe(new AgentResponseUpdate(ChatRole.Tool, [functionResult]));

        Activity toolActivity = Assert.Single(collector.Stopped, static a => string.Equals(a.DisplayName, "execute_tool team_lookup", StringComparison.Ordinal));
        Assert.Equal("execute_tool", toolActivity.GetTagItem("gen_ai.operation.name"));
        Assert.Equal("foundry-agent", toolActivity.GetTagItem("gen_ai.agent.name"));
        Assert.Equal("call-1", toolActivity.GetTagItem("gen_ai.tool.call.id"));
        Assert.Equal("ok", toolActivity.GetTagItem("gen_ai.tool.call.result"));
    }

    [Fact]
    public void DisposeUnfinishedToolCallIsMarkedIncomplete()
    {
        using ActivityCollector collector = new("traceability-tests.tools.incomplete");
        using ActivitySource source = new("traceability-tests.tools.incomplete");

        using (ToolTelemetryTracker tracker = new(source, "local-agent"))
        {
            FunctionCallContent functionCall = new(
                callId: "call-2",
                name: "statbotics_lookup",
                arguments: new Dictionary<string, object?>
                {
                    ["team"] = 2046,
                });
            tracker.Observe(new AgentResponseUpdate(ChatRole.Assistant, [functionCall]));
        }

        Activity toolActivity = Assert.Single(collector.Stopped, static a => string.Equals(a.DisplayName, "execute_tool statbotics_lookup", StringComparison.Ordinal));
        Assert.Equal(true, toolActivity.GetTagItem("gen_ai.tool.call.incomplete"));
    }
}
