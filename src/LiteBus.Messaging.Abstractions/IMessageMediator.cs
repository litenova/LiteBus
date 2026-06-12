using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines the core message mediation interface responsible for routing messages to their appropriate handlers.
/// </summary>
/// <remarks>
///     The message mediator is the central component of the LiteBus messaging system. It receives messages,
///     locates the appropriate handlers, and orchestrates the execution of the message handling pipeline,
///     including pre-handlers, main handlers, post-handlers, and error handlers.
/// </remarks>
public interface IMessageMediator
{
    /// <summary>
    ///     Mediates a message by routing it to the appropriate handler and executing the message handling pipeline.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to be mediated.</typeparam>
    /// <typeparam name="TMessageResult">The type of result expected from the mediation process.</typeparam>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="request">Configuration that controls the mediation behavior.</param>
    /// <param name="cancellationToken">The token used to cancel the mediation process.</param>
    /// <returns>The result of the mediation process, of type <typeparamref name="TMessageResult" />.</returns>
    /// <remarks>
    ///     The mediation process includes executing pre-handlers, the main handler, post-handlers, and error handlers if
    ///     exceptions occur.
    ///     The specific behavior is determined by the mediation strategy specified in the request.
    /// </remarks>
    TMessageResult Mediate<TMessage, TMessageResult>(
        TMessage message,
        MessageMediationRequest<TMessage, TMessageResult> request,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    ///     Asynchronously mediates a message by routing it to the appropriate handler and executing the message handling pipeline.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to be mediated.</typeparam>
    /// <typeparam name="TMessageResult">The type of result expected from the mediation process.</typeparam>
    /// <param name="message">The message to mediate.</param>
    /// <param name="request">Configuration that controls the mediation behavior.</param>
    /// <param name="cancellationToken">The token used to cancel the mediation process.</param>
    /// <returns>A task that completes with the mediation result.</returns>
    /// <remarks>
    ///     Prefer this method over <see cref="Mediate{TMessage, TMessageResult}" /> when the mediation strategy returns
    ///     <see cref="Task" /> or <see cref="Task{TResult}" /> so callers can await completion without blocking.
    /// </remarks>
    Task<TMessageResult> MediateAsync<TMessage, TMessageResult>(
        TMessage message,
        MessageMediationRequest<TMessage, TMessageResult> request,
        CancellationToken cancellationToken = default)
        where TMessage : notnull;
}
