using System;
using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents options that control the behavior of the message mediation process.
/// </summary>
/// <typeparam name="TMessage">The type of the message being mediated.</typeparam>
/// <typeparam name="TMessageResult">The type of the result expected from the mediation process.</typeparam>
/// <remarks>
///     These options allow customizing various aspects of the mediation process, such as
///     how message types are resolved, which mediation strategy is used, and what tags are applied
///     for filtering handlers.
/// </remarks>
public sealed class MediateOptions<TMessage, TMessageResult> where TMessage : notnull
{
    /// <summary>
    ///     Gets or initializes the strategy used to resolve message descriptors from the message registry.
    /// </summary>
    /// <remarks>
    ///     The message resolve strategy determines how a message type is matched to a descriptor in the registry.
    /// </remarks>
    public required IMessageResolveStrategy MessageResolveStrategy { get; init; }

    /// <summary>
    ///     Gets or initializes the strategy used to mediate the message.
    /// </summary>
    /// <remarks>
    ///     The mediation strategy determines how the message is processed, including how handlers are invoked
    ///     and how results are produced.
    /// </remarks>
    public required IMessageMediationStrategy<TMessage, TMessageResult> MessageMediationStrategy { get; init; }

    /// <summary>
    ///     Gets or initializes the cancellation token used to cancel the mediation process.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    ///     Gets or initializes the collection of tags used to filter handlers during mediation.
    /// </summary>
    /// <remarks>
    ///     When tags are specified, only handlers with matching tags will participate in the mediation process.
    /// </remarks>
    public required IEnumerable<string> Tags { get; init; }

    /// <summary>
    ///     Gets or initializes a value indicating whether to register plain messages on the spot.
    /// </summary>
    /// <remarks>
    ///     Plain messages are messages that do not implement any specific message interfaces.
    ///     When this option is enabled, such messages will be automatically registered in the registry
    ///     when they are first encountered during mediation.
    /// </remarks>
    public bool RegisterPlainMessagesOnSpot { get; init; } = false;

    /// <summary>
    /// Gets or initializes a predicate function used to filter event handlers by their descriptor.
    /// </summary>
    /// <remarks>
    /// This predicate is evaluated for each potential handler descriptor before execution.
    /// Use this for advanced filtering scenarios beyond tag-based filtering.
    /// The predicate is applied after tag filtering.
    /// </remarks>
    public Func<IHandlerDescriptor, bool> HandlerPredicate { get; init; } = _ => true;
}