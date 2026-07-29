using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Disposes the dispatch scope retained for one mediation call.
/// </summary>
internal sealed class MediationResourceScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets the dispatch scope that owns scoped handler instances.
    /// </summary>
    private readonly IMessageDispatchScope _dispatchScope;

    /// <summary>
    ///     Tracks whether the resource scope has already been disposed.
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationResourceScope" /> class.
    /// </summary>
    /// <param name="dispatchScope">The dispatch scope that owns scoped handler instances.</param>
    public MediationResourceScope(
        IMessageDispatchScope dispatchScope)
    {
        ArgumentNullException.ThrowIfNull(dispatchScope);
        _dispatchScope = dispatchScope;
    }

    /// <summary>
    ///     Disposes the dispatch scope once.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _dispatchScope.Dispose();
    }

    /// <summary>
    ///     Asynchronously disposes the dispatch scope once.
    /// </summary>
    /// <returns>A value task representing asynchronous dispatch-scope disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _dispatchScope.DisposeAsync().ConfigureAwait(false);
    }
}
