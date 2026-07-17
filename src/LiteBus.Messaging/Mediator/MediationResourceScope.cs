using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Disposes ambient execution context and optional dispatch scopes retained for one mediation call.
/// </summary>
internal sealed class MediationResourceScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets the ambient execution context scope created for the mediation call.
    /// </summary>
    private readonly AmbientExecutionContext.ExecutionContextScope _executionScope;

    /// <summary>
    ///     Gets the dispatch scope that owns scoped handler instances, when one was created.
    /// </summary>
    private readonly IMessageDispatchScope? _dispatchScope;

    /// <summary>
    ///     Tracks whether the resource scope has already been disposed.
    /// </summary>
    private int _disposed;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationResourceScope" /> class.
    /// </summary>
    /// <param name="executionScope">The ambient execution context scope created for the mediation call.</param>
    /// <param name="dispatchScope">The dispatch scope that owns scoped handler instances, when one was created.</param>
    public MediationResourceScope(
        AmbientExecutionContext.ExecutionContextScope executionScope,
        IMessageDispatchScope? dispatchScope)
    {
        _executionScope = executionScope;
        _dispatchScope = dispatchScope;
    }

    /// <summary>
    ///     Disposes the ambient execution context and dispatch scopes once.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _executionScope.Dispose();
        _dispatchScope?.Dispose();
    }

    /// <summary>
    ///     Asynchronously disposes the ambient execution context and dispatch scopes once.
    /// </summary>
    /// <returns>A value task representing asynchronous dispatch-scope disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _executionScope.Dispose();

        if (_dispatchScope is not null)
        {
            await _dispatchScope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
