using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a pre-handler that decides whether a message that produces a result reaches its main handler.
/// </summary>
/// <typeparam name="TMessage">The type of message this gate runs for.</typeparam>
/// <typeparam name="TMessageResult">The result type of the message, which the directive is typed over.</typeparam>
/// <remarks>
///     <para>
///         Stopping the pipeline means the main handler never runs, so the gate has to supply the value the caller
///         receives. Typing the directive over <typeparamref name="TMessageResult" /> is what makes the compiler check
///         that value instead of the pipeline rejecting it at dispatch time.
///     </para>
///     <para>
///         Return <see cref="PipelineDirective{TMessageResult}.Continue" /> to proceed,
///         <see cref="PipelineDirective{TMessageResult}.ShortCircuit" /> to answer without running the handler, or one
///         of the <c>Deny</c> overloads to refuse the message.
///     </para>
/// </remarks>
public interface IMessageGate<in TMessage, TMessageResult> : IMessagePreHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Decides whether the mediation proceeds to the main handler.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The directive that tells the pipeline whether to continue, and the result to return when it does not.</returns>
    Task<PipelineDirective<TMessageResult>> DecideAsync(TMessage message, CancellationToken cancellationToken = default);
}
