using Microsoft.Extensions.Hosting;

namespace LiteBus.Runtime.UnitTests;

/// <summary>
///     Records host stop requests for background service supervision tests.
/// </summary>
internal sealed class RecordingHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();

    private readonly CancellationTokenSource _stopped = new();

    private readonly CancellationTokenSource _stopping = new();

    /// <inheritdoc />
    public CancellationToken ApplicationStarted => _started.Token;

    /// <inheritdoc />
    public CancellationToken ApplicationStopped => _stopped.Token;

    /// <inheritdoc />
    public CancellationToken ApplicationStopping => _stopping.Token;

    /// <summary>
    ///     Gets the number of application stop requests received by this lifetime.
    /// </summary>
    public int StopApplicationCallCount { get; private set; }

    /// <inheritdoc />
    public void StopApplication()
    {
        StopApplicationCallCount++;
        _stopping.Cancel();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _started.Dispose();
        _stopped.Dispose();
        _stopping.Dispose();
    }
}
