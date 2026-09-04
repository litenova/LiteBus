using System.Diagnostics;
using LiteBus.Messaging;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Collects the mediation spans started while it is alive.
/// </summary>
/// <remarks>
///     An <see cref="ActivitySource" /> records nothing unless something listens, which is what makes the default
///     configuration cheap. A test asserting on spans therefore has to listen, and this is that listener.
/// </remarks>
internal sealed class RecordedActivities : IDisposable
{
    /// <summary>
    ///     The listener subscribed to the mediation source.
    /// </summary>
    private readonly ActivityListener _listener;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordedActivities" /> class.
    /// </summary>
    public RecordedActivities()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == LiteBusMediationTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Activities.Add(activity)
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    ///     Gets the spans that were stopped, in the order they stopped.
    /// </summary>
    public List<Activity> Activities { get; } = [];

    /// <inheritdoc />
    public void Dispose()
    {
        _listener.Dispose();
    }
}
