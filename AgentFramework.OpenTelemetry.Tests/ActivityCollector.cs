namespace AgentFramework.OpenTelemetry.Tests;

using System.Diagnostics;

internal sealed class ActivityCollector : IDisposable
{
    private readonly HashSet<string> _sourceNames;
    private readonly ActivityListener _listener;

    public ActivityCollector(params string[] sourceNames)
    {
        ArgumentNullException.ThrowIfNull(sourceNames);

        _sourceNames = [.. sourceNames];
        _listener = new ActivityListener
        {
            ShouldListenTo = ShouldListenTo,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = this.OnActivityStarted,
            ActivityStopped = this.OnActivityStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public List<Activity> Started { get; } = [];

    public List<Activity> Stopped { get; } = [];

    public void Dispose() => _listener.Dispose();

    private bool ShouldListenTo(ActivitySource source) => _sourceNames.Contains(source.Name);

    private void OnActivityStarted(Activity activity) => Started.Add(activity);

    private void OnActivityStopped(Activity activity) => Stopped.Add(activity);
}
