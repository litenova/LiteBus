using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies the received message carries the queue it was consumed from.
/// </summary>
public sealed class AmqpReceivedMessageQueueNameTests
{
    /// <summary>
    ///     Verifies the queue name is null when the consumer did not track it.
    /// </summary>
    [Fact]
    public void QueueName_WhenNotSet_ShouldBeNull()
    {
        var message = new AmqpReceivedMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = new Dictionary<string, object?>(),
            DeliveryTag = 7,
            AckDelegate = (_, _) => Task.CompletedTask,
            NackDelegate = (_, _, _) => Task.CompletedTask
        };

        message.QueueName.Should().BeNull();
    }

    /// <summary>
    ///     Verifies the queue name round-trips for ingress destination mapping.
    /// </summary>
    [Fact]
    public void QueueName_WhenSet_ShouldRoundTrip()
    {
        var message = new AmqpReceivedMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = new Dictionary<string, object?>(),
            DeliveryTag = 7,
            QueueName = "orders",
            AckDelegate = (_, _) => Task.CompletedTask,
            NackDelegate = (_, _, _) => Task.CompletedTask
        };

        message.QueueName.Should().Be("orders");
    }
}
