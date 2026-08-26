using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a message reaches its main handler.
/// </summary>
/// <typeparam name="TMessage">The type of message this gate runs for.</typeparam>
/// <remarks>
///     <para>
///         A gate runs in the pre-handler stage, ordered among the other pre-handlers by priority, and returns
///         <see cref="PipelineDirective.Continue" /> to let the mediation proceed,
///         <see cref="PipelineDirective.ShortCircuit" /> to skip work whose result is already known, or
///         <see cref="PipelineDirective.Deny" /> to refuse the message.
///     </para>
///     <para>
///         Because the decision is a return value, the compiler requires it. Nothing after the decision runs by
///         accident, and an expected control-flow path stays off the exception path.
///     </para>
///     <para>
///         This shape is for messages that produce no result. Use <see cref="IMessageGate{TMessage,TMessageResult}" />
///         for a message that produces one, so the compiler checks the value the gate supplies.
///     </para>
///     <para>
///         Deciding belongs to the pre-handler stage alone, because stopping means skipping the work. A handler that
///         wants to suppress the reactions to a no-op calls <see cref="IExecutionContext.SuppressPostHandlers" />
///         instead.
///     </para>
/// </remarks>
public interface IMessageGate<in TMessage> : IMessagePreHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Decides whether the mediation proceeds to the main handler.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive that tells the pipeline whether to continue.</returns>
    Task<PipelineDirective> DecideAsync(TMessage message, CancellationToken cancellationToken = default);
}
