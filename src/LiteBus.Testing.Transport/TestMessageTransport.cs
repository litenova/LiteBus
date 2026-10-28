using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     In-memory transport double with controllable publish outcomes and recorded publications.
/// </summary>
/// <remarks>
///     All members are thread-safe. The one-shot failure is consumed atomically, so exactly one concurrent
///     <see cref="PublishAsync" /> call observes it.
/// </remarks>
public sealed class TestMessageTransport : ITransportPublisher
{
    /// <summary>
    ///     Gets the publications recorded by <see cref="PublishAsync" />.
    /// </summary>
    private readonly ConcurrentQueue<TransportPublishRequest> _published = new();

    /// <summary>
    ///     The exception thrown on the next <see cref="PublishAsync" /> call, consumed atomically.
    /// </summary>
    private Exception? _nextPublishException;

    /// <summary>
    ///     Whether the transport is disconnected, read and written with volatile semantics.
    /// </summary>
    private volatile bool _isDisconnected;

    /// <summary>
    ///     Gets or sets the exception thrown on the next <see cref="PublishAsync" /> call.
    ///     Set to <see langword="null" /> for successful publish.
    /// </summary>
    /// <value>
    ///     The failure is single-use: each <see cref="PublishAsync" /> call consumes it atomically, so exactly one
    ///     concurrent publish observes a scripted exception.
    /// </value>
    public Exception? NextPublishException
    {
        get => Volatile.Read(ref _nextPublishException);
        set => Volatile.Write(ref _nextPublishException, value);
    }

    /// <summary>
    ///     Gets or sets a value indicating whether the transport is disconnected.
    ///     When <see langword="true" />, <see cref="PublishAsync" /> throws <see cref="InvalidOperationException" />.
    /// </summary>
    public bool IsDisconnected
    {
        get => _isDisconnected;
        set => _isDisconnected = value;
    }

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
            throw new InvalidOperationException("Test transport is disconnected.");
        }

        var exception = Interlocked.Exchange(ref _nextPublishException, null);

        if (exception is not null)
        {
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

        Interlocked.Exchange(ref _nextPublishException, null);
        IsDisconnected = false;
    }
}
