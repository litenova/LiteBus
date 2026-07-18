using System.Text.Json;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Runtime.UnitTests.Serialization;

/// <summary>
///     Verifies JSON message serialization, cancellation, configuration, and failure mapping.
/// </summary>
public sealed class SystemTextJsonMessageSerializerTests
{
    /// <summary>
    ///     Verifies default web JSON options round-trip the runtime message type.
    /// </summary>
    [Fact]
    public async Task SerializeAndDeserializeAsync_WithDefaultOptions_RoundTripsMessage()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        object message = new TestMessage("order-42", 3);

        var payload = await serializer.SerializeAsync(message).ConfigureAwait(false);
        var deserialized = await serializer.DeserializeAsync(typeof(TestMessage), payload).ConfigureAwait(false);

        payload.Should().Contain("\"orderId\":\"order-42\"");
        deserialized.Should().BeEquivalentTo(message);
    }

    /// <summary>
    ///     Verifies caller-supplied JSON options control the serialized property names.
    /// </summary>
    [Fact]
    public async Task SerializeAsync_WithCustomOptions_UsesThoseOptions()
    {
        var serializer = new SystemTextJsonMessageSerializer(new JsonSerializerOptions
        {
            PropertyNamingPolicy = null
        });

        var payload = await serializer.SerializeAsync(new TestMessage("order-42", 3)).ConfigureAwait(false);

        payload.Should().Contain("\"OrderId\":\"order-42\"");
    }

    /// <summary>
    ///     Verifies unsupported runtime types are wrapped with serialization context.
    /// </summary>
    [Fact]
    public async Task SerializeAsync_WhenRuntimeTypeIsUnsupported_ThrowsMessageSerializationException()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        Action message = () =>
        {
        };

        var action = () => serializer.SerializeAsync(message);

        var exception = await action.Should().ThrowAsync<MessageSerializationException>().ConfigureAwait(false);
        exception.Which.MessageType.Should().Be(message.GetType());
        exception.Which.Operation.Should().Be("serialized");
        exception.Which.InnerException.Should().BeOfType<NotSupportedException>();
    }

    /// <summary>
    ///     Verifies malformed JSON is wrapped with the requested message type.
    /// </summary>
    [Fact]
    public async Task DeserializeAsync_WhenPayloadIsMalformed_ThrowsMessageSerializationException()
    {
        var serializer = new SystemTextJsonMessageSerializer();

        var action = () => serializer.DeserializeAsync(typeof(TestMessage), "{invalid");

        var exception = await action.Should().ThrowAsync<MessageSerializationException>().ConfigureAwait(false);
        exception.Which.MessageType.Should().Be(typeof(TestMessage));
        exception.Which.Operation.Should().Be("deserialized");
        exception.Which.InnerException.Should().BeOfType<JsonException>();
    }

    /// <summary>
    ///     Verifies a JSON null payload is rejected instead of returning a null message object.
    /// </summary>
    [Fact]
    public async Task DeserializeAsync_WhenPayloadIsJsonNull_ThrowsMessageSerializationException()
    {
        var serializer = new SystemTextJsonMessageSerializer();

        var action = () => serializer.DeserializeAsync(typeof(TestMessage), "null");

        await action.Should().ThrowAsync<MessageSerializationException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies cancellation is observed before JSON conversion begins.
    /// </summary>
    [Fact]
    public async Task Operations_WhenCancellationIsRequested_ThrowOperationCanceledException()
    {
        var serializer = new SystemTextJsonMessageSerializer();
        using var source = new CancellationTokenSource();
        await source.CancelAsync().ConfigureAwait(false);

        var serialize = () => serializer.SerializeAsync(new TestMessage("order-42", 3), source.Token);
        var deserialize = () => serializer.DeserializeAsync(typeof(TestMessage), "{}", source.Token);

        await serialize.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
        await deserialize.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies null inputs are rejected at the serializer boundary.
    /// </summary>
    [Fact]
    public async Task Operations_WhenInputIsNull_ThrowArgumentNullException()
    {
        var serializer = new SystemTextJsonMessageSerializer();

        var serialize = () => serializer.SerializeAsync<string>(null!);
        var nullType = () => serializer.DeserializeAsync(null!, "{}");
        var nullPayload = () => serializer.DeserializeAsync(typeof(TestMessage), null!);

        await serialize.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        await nullType.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        await nullPayload.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    private sealed record TestMessage(string OrderId, int Attempt);
}
