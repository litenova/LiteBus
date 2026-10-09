using LiteBus.Messaging.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Storage.UnitTests.Saga;

/// <summary>
///     Verifies saga state that deserializes to an unexpected type raises a typed serialization exception.
/// </summary>
public sealed class SagaStateCastTests
{
    /// <summary>
    ///     Verifies loading state that deserializes to another type raises <see cref="MessageSerializationException" />.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WhenPayloadDeserializesToAnotherType_ThrowsMessageSerializationException()
    {
        var store = new InMemorySagaStore(new WrongTypeSerializer());
        var correlation = new SagaCorrelation
        {
            CorrelationId = "order-42",
            SagaDefinitionId = "orders"
        };
        await store.SaveAsync(SagaSaveItem<CastSagaState>.From(
            correlation,
            new CastSagaState { Step = 1 },
            0)).ConfigureAwait(false);

        MessageSerializationException? thrown = null;

        try
        {
            await store.LoadAsync<CastSagaState>(correlation).ConfigureAwait(false);
        }
        catch (MessageSerializationException exception)
        {
            thrown = exception;
        }

        thrown.Should().NotBeNull();
        thrown!.MessageType.Should().Be(typeof(CastSagaState));
        thrown.Operation.Should().Be("deserialized");
        thrown.InnerException.Should().BeOfType<InvalidCastException>();
    }

    /// <summary>
    ///     Saga state tracked for state cast tests.
    /// </summary>
    private sealed class CastSagaState
    {
        /// <summary>
        ///     Gets or sets the current step.
        /// </summary>
        public int Step { get; set; }
    }

    /// <summary>
    ///     Unrelated state type returned by the wrong-type serializer.
    /// </summary>
    private sealed class UnrelatedSagaState
    {
        /// <summary>
        ///     Gets or sets the unrelated value.
        /// </summary>
        public string? Value { get; set; }
    }

    /// <summary>
    ///     Serializer that always deserializes to an unrelated state type.
    /// </summary>
    private sealed class WrongTypeSerializer : IMessageSerializer
    {
        /// <inheritdoc />
        public Task<string> SerializeAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            return Task.FromResult("{}");
        }

        /// <inheritdoc />
        public Task<object> DeserializeAsync(Type messageType, string payload, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<object>(new UnrelatedSagaState());
        }
    }
}
