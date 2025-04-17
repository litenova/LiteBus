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
    /// <param name="options">Configuration options that control the mediation behavior.</param>
    /// <returns>The result of the mediation process, of type <typeparamref name="TMessageResult" />.</returns>
    /// <remarks>
    ///     The mediation process includes executing pre-handlers, the main handler, post-handlers, and error handlers if
    ///     exceptions occur.
    ///     The specific behavior is determined by the mediation strategy specified in the options.
    /// </remarks>
    TMessageResult Mediate<TMessage, TMessageResult>(TMessage message, MediateOptions<TMessage, TMessageResult> options)
        where TMessage : notnull;
}