using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when a message descriptor cannot be resolved after optional on-the-spot registration.
/// </summary>
[Serializable]
public sealed class MessageDescriptorNotFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDescriptorNotFoundException" /> class.
    /// </summary>
    /// <param name="messageType">The message type being mediated.</param>
    /// <param name="resolveStrategyType">The resolve strategy type used for descriptor lookup.</param>
    /// <param name="registerPlainMessagesOnSpot">Whether plain messages were registered on the spot.</param>
    /// <param name="registeredMessageCount">The number of message descriptors in the registry.</param>
    public MessageDescriptorNotFoundException(Type messageType,
                                              Type resolveStrategyType,
                                              bool registerPlainMessagesOnSpot,
                                              int registeredMessageCount)
        : base($"No descriptor found for message type '{messageType.FullName ?? messageType.Name}' using resolve strategy '{resolveStrategyType.FullName ?? resolveStrategyType.Name}'. RegisterPlainMessagesOnSpot: {registerPlainMessagesOnSpot}. Registered message count: {registeredMessageCount}.")
    {
        MessageType = messageType;
        ResolveStrategyType = resolveStrategyType;
        RegisterPlainMessagesOnSpot = registerPlainMessagesOnSpot;
        RegisteredMessageCount = registeredMessageCount;
    }

    /// <summary>
    ///     Gets the message type being mediated.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Gets the resolve strategy type used for descriptor lookup.
    /// </summary>
    public Type ResolveStrategyType { get; }

    /// <summary>
    ///     Gets a value indicating whether plain messages were registered on the spot.
    /// </summary>
    public bool RegisterPlainMessagesOnSpot { get; }

    /// <summary>
    ///     Gets the number of registered message descriptors.
    /// </summary>
    public int RegisteredMessageCount { get; }
}