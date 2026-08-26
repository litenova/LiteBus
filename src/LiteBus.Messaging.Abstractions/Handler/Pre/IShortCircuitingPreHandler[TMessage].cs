using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that may stop the pipeline before the main handler runs.
/// </summary>
/// <typeparam name="TMessage">The type of message this pre-handler runs for.</typeparam>
/// <remarks>
///     <para>
///         Return <see cref="PipelineDirective.Continue" /> to let the mediation proceed, or
///         <see cref="PipelineDirective.ShortCircuit" /> to stop it and supply the result the caller receives. The
///         mediation reports <see cref="MessageOutcome.Aborted" />, which means the main handler never ran.
///     </para>
///     <para>
///         Because the decision is a return value, the compiler requires it. Nothing after the decision runs by
///         accident, and an expected control-flow path stays off the exception path.
///     </para>
///     <para>
///         Short-circuiting belongs to the pre-handler stage alone. A handler that wants to suppress the reactions to a
///         no-op calls <see cref="IExecutionContext.SuppressPostHandlers" /> instead.
///     </para>
/// </remarks>
public interface IShortCircuitingPreHandler<in TMessage> : IMessagePreHandler
    where TMessage : notnull
{
    /// <inheritdoc />
    Task<PipelineDirective> IMessagePreHandler.PreHandleAsync(object message, CancellationToken cancellationToken)
    {
        return PreHandleAsync((TMessage) message, cancellationToken);
    }

    /// <summary>
    ///     Runs before the main handler and decides whether the pipeline proceeds.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive that tells the pipeline whether to continue.</returns>
    Task<PipelineDirective> PreHandleAsync(TMessage message, CancellationToken cancellationToken = default);
}
