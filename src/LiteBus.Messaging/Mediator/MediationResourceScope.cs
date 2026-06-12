using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Messaging.Mediator;

/// <summary>
///     Disposes ambient execution context and optional dispatch scopes retained for one mediation call.
/// </summary>
internal sealed class MediationResourceScope : IDisposable
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
        _executionScope.Dispose();
        _dispatchScope?.Dispose();
    }
}
