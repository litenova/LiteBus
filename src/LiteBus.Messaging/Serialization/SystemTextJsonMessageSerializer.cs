using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

/// <summary>
///     Serializes durable messages with System.Text.Json.
/// </summary>
public sealed class SystemTextJsonMessageSerializer : IMessageSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemTextJsonMessageSerializer" /> class.
    /// </summary>
    /// <param name="jsonSerializerOptions">The serializer options. When omitted, web defaults are used.</param>
    public SystemTextJsonMessageSerializer(JsonSerializerOptions? jsonSerializerOptions = null)
    {
        _jsonSerializerOptions = jsonSerializerOptions ?? DefaultOptions;
    }

    /// <inheritdoc />
    public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            var payload = JsonSerializer.Serialize(message, message.GetType(), _jsonSerializerOptions);
            return Task.FromResult(payload);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new MessageSerializationException(message.GetType(), "serialized", exception);
        }
    }

    /// <inheritdoc />
    public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var message = JsonSerializer.Deserialize(payload, messageType, _jsonSerializerOptions)
                          ?? throw new JsonException("The deserialized payload was null.");

            return Task.FromResult(message);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new MessageSerializationException(messageType, "deserialized", exception);
        }
    }
}