using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a message resolve strategy finds more than one equally specific assignable descriptor.
/// </summary>
public sealed class AmbiguousMessageResolveException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AmbiguousMessageResolveException" /> class.
    /// </summary>
    /// <param name="messageType">The runtime message type that could not be resolved unambiguously.</param>
    /// <param name="resolveStrategyType">The resolve strategy that detected the ambiguity.</param>
    public AmbiguousMessageResolveException(Type messageType, Type resolveStrategyType)
        : base(
            $"More than one message descriptor is assignable from '{messageType.FullName ?? messageType.Name}' " +
            $"using resolve strategy '{resolveStrategyType.FullName ?? resolveStrategyType.Name}'. " +
            "Register handlers for a single most-derived message type or use a more specific resolve strategy.")
    {
        MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
        ResolveStrategyType = resolveStrategyType ?? throw new ArgumentNullException(nameof(resolveStrategyType));
    }

    /// <summary>
    ///     Gets the runtime message type that could not be resolved unambiguously.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Gets the resolve strategy that detected the ambiguity.
    /// </summary>
    public Type ResolveStrategyType { get; }
}
