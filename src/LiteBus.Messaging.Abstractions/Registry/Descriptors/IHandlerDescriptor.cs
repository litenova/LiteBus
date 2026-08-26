using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a handler, providing metadata about the handler such as the message type it handles,
///     its execution order, and any associated tags.
/// </summary>
public interface IHandlerDescriptor
{
    /// <summary>
    ///     Gets the type of the message that the handler is associated with. If the message type is generic,
    ///     this property returns the generic type definition.
    /// </summary>
    Type MessageType { get; }

    /// <summary>
    ///     Gets the order/priority in which the handler should be executed. Handlers with lower order values are executed
    ///     first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    ///     Gets the registration sequence assigned when the handler descriptor was committed to the message registry.
    ///     Lower values indicate earlier module registration and act as a stable tiebreaker after <see cref="Priority" />.
    /// </summary>
    int RegistrationSequence { get; }

    /// <summary>
    ///     Gets a collection of tags associated with the handler. Tags can be used to categorize or identify handlers in a
    ///     flexible way.
    /// </summary>
    IReadOnlyCollection<string> Tags { get; }

    /// <summary>
    ///     Gets the type of the handler. This represents the actual implementation type of the handler.
    /// </summary>
    Type HandlerType { get; }

    /// <summary>
    ///     Gets the closed handler contract this descriptor was discovered from, such as
    ///     <c>IMessagePostHandler&lt;PlaceOrderCommand, OrderId&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     A handler class may implement contracts for several message types, and may implement more than one contract
    ///     for the same message type. Recording the exact contract is what lets the pipeline invoke the handler through
    ///     the one it was registered under rather than guessing, and it names the registration in diagnostics. The
    ///     contract is open when the handler is registered for a generic message, because the message type is a generic
    ///     type definition until a concrete message arrives.
    /// </remarks>
    Type ContractType { get; }
}