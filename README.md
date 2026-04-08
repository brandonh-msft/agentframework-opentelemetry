# AgentFramework.OpenTelemetry

`AgentFramework.OpenTelemetry` is a reusable OpenTelemetry layer for Microsoft Agent Framework workflows that coordinates:

- hosted/persistent Foundry agents
- local in-process declarative agents
- tool execution across both

It preserves end-to-end trace continuity (`chat_turn` → `invoke_agent` → `execute_tool`) and emits explicit workflow handoff events.

## What it provides

- `Activities`: traceparent parsing/formatting, preferred parent resolution, conversation root/context helpers
- `W3CTraceContextAzureHttpPipelinePolicy` + `W3CTraceContextClientModelPipelinePolicy`: W3C propagation into Azure/Core + ClientModel pipelines
- `FoundryAgent` + `LocalAgent`: trace-aware wrappers for remote and local agents
- `WorkflowHandoffTelemetry`: emits `gen_ai.workflow.handoff` events with from/to/reason tags
- `OpenTelemetryExtensions`: one-line tracing bootstrap helpers

Host-specific Azure SDK filtering, such as suppressing `Azure.Identity` spans, is intentionally left to the consuming application rather than being bundled into this library.

## Minimal wiring

```csharp
using AgentFramework.OpenTelemetry;
using AgentFramework.OpenTelemetry.Agents;

OpenTelemetryExtensions.EnableAzureExperimentalTracing();

Sdk.CreateTracerProviderBuilder()
   .AddAgentFrameworkOpenTelemetry("My.Agent.Service")
   .AddAzureMonitorTraceExporter()
   .Build();

clientOptions.AddPolicy(
    W3CTraceContextClientModelPipelinePolicy.Instance,
    System.ClientModel.Primitives.PipelinePosition.PerTry);

persistentOptions.AddPolicy(
    W3CTraceContextAzureHttpPipelinePolicy.Instance,
    Azure.Core.Pipeline.HttpPipelinePosition.PerRetry);
```

From there, wrap your agents with `FoundryAgent` / `LocalAgent`, and emit handoff events when the workflow routes work between them.

## GitHub Actions

- `CI` restores, builds, and tests the solution on Ubuntu and Windows for pushes to `main` and all pull requests.
- `Release` runs only for pushes to `main`, creates the `.nupkg` and `.snupkg` artifacts, publishes the package to GitHub Packages, and creates a tagged GitHub release with commit-based change notes.
