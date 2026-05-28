using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Serializes and deserializes LiteBus messages.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    ///     Serializes a message instance.
    /// </summary>
    /// <typeparam name="TMessage">The compile-time message type.</typeparam>
    /// <param name="message">The message instance.</param>
    /// <param name="cancellationToken">A token used to cancel serialization.</param>
    /// <returns>The serialized payload.</returns>
    Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    ///     Deserializes a payload into the requested message type.
    /// </summary>
    /// <param name="messageType">The requested CLR message type.</param>
    /// <param name="payload">The serialized payload.</param>
    /// <param name="cancellationToken">A token used to cancel deserialization.</param>
    /// <returns>The deserialized message instance.</returns>
    Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default);
}