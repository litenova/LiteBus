using System.Collections.Concurrent;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     In-memory transport double with controllable publish outcomes and recorded publications.
/// </summary>
public sealed class TestMessageTransport : IMessageTransport
{
    /// <summary>
    ///     Gets the publications recorded by <see cref="PublishAsync" />.
    /// </summary>
    private readonly ConcurrentQueue<TransportPublishRequest> _published = new();

    /// <summary>
    ///     Gets or sets the exception thrown on the next <see cref="PublishAsync" /> call.
    ///     Set to <see langword="null" /> for successful publish.
    /// </summary>
    public Exception? NextPublishException { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the transport is disconnected.
    ///     When <see langword="true" />, <see cref="PublishAsync" /> throws <see cref="InvalidOperationException" />.
    /// </summary>
    public bool IsDisconnected { get; set; }

    /// <summary>
    ///     Gets the publications recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyCollection<TransportPublishRequest> Published => _published.ToArray();

    /// <inheritdoc />
    public Task PublishAsync(TransportPublishRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsDisconnected)
        {
            throw new InvalidOperationException("Fake transport is disconnected.");
        }

        if (NextPublishException is not null)
        {
            var exception = NextPublishException;
            NextPublishException = null;
            throw exception;
        }

        _published.Enqueue(request);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Clears recorded publications and resets failure state.
    /// </summary>
    public void Clear()
    {
        while (_published.TryDequeue(out _))
        {
        }

        NextPublishException = null;
        IsDisconnected = false;
    }
}