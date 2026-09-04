using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Thrown when <see cref="IMessageMetadataAccessor" /> is asked about a type the message registry does not hold.
/// </summary>
/// <remarks>
///     Metadata is resolved when a message type is registered, so a type with no descriptor has no declarations to
///     read. Reporting that is deliberate: answering with an empty collection instead would turn a missing registration
///     into a cross-cutting check that silently passes, which is the worst possible failure for a permission guard.
/// </remarks>
public sealed class MessageMetadataNotFoundException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageMetadataNotFoundException" /> class.
    /// </summary>
    /// <param name="messageType">The type whose metadata was requested.</param>
    public MessageMetadataNotFoundException(Type messageType)
        : base(BuildMessage(messageType))
    {
        MessageType = messageType;
    }

    /// <summary>
    ///     Gets the type whose metadata was requested.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    ///     Builds the exception message, naming the type and the registration that is missing.
    /// </summary>
    /// <param name="messageType">The type whose metadata was requested.</param>
    /// <returns>The exception message.</returns>
    private static string BuildMessage(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return $"'{messageType.FullName ?? messageType.Name}' is not registered as a message, so it has no declared "
               + "metadata to read. Register it with the command, query, or event module builder, or with "
               + "RegisterFromAssembly.";
    }
}
