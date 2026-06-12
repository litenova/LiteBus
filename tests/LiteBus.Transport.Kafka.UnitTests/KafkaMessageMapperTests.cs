using System.Text;
using AwesomeAssertions;
using Confluent.Kafka;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.Kafka.UnitTests;

/// <summary>
///     Verifies Kafka message mapping for publish and consume paths.
/// </summary>
public sealed class KafkaMessageMapperTests
{
    /// <summary>
    ///     Verifies publish requests map topic key, body, and LiteBus headers to Kafka record headers.
    /// </summary>
    [Fact]
    public void ToKafkaMessage_ShouldMapKeyBodyAndHeaders()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders",
            Route = "tenant-a",
            Body = Encoding.UTF8.GetBytes("""{"orderId":"3"}"""),
            MessageId = Guid.NewGuid().ToString("D"),
            CorrelationId = "corr-kafka",
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "orders.commands.ship"
            }
        };

        var kafkaMessage = KafkaMessageMapper.ToKafkaMessage(request);

        kafkaMessage.Key.Should().Be("tenant-a");
        Encoding.UTF8.GetString(kafkaMessage.Value).Should().Contain("orderId");

        KafkaMessageMapperTestsHelpers.GetHeader(kafkaMessage.Headers, TransportHeaders.ContractName)
            .Should().Be("orders.commands.ship");

        KafkaMessageMapperTestsHelpers.GetHeader(kafkaMessage.Headers, TransportHeaders.MessageId)
            .Should().Be(request.MessageId);
    }

    /// <summary>
    ///     Verifies consumed records map to transport messages with commit delegates.
    /// </summary>
    [Fact]
    public async Task ToTransportMessage_ShouldExposeCommitDelegate()
    {
        var committed = false;

        var result = new ConsumeResult<string, byte[]>
        {
            Topic = "orders",
            Message = new Message<string, byte[]>
            {
                Key = "tenant-a",
                Value = Encoding.UTF8.GetBytes("payload"),
                Headers = new Headers
                {
                    { TransportHeaders.ContractName, Encoding.UTF8.GetBytes("orders.commands.ship") }
                }
            }
        };

        var transportMessage = KafkaMessageMapper.ToTransportMessage(
            result,
            "orders",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ =>
                {
                    committed = true;
                    return Task.CompletedTask;
                },
                NackAsync = (_, _) => Task.CompletedTask
            },
            redelivered: false);

        transportMessage.Route.Should().Be("tenant-a");
        transportMessage.Headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");

        await transportMessage.AcceptAsync().ConfigureAwait(false);
        committed.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies <see cref="TransportMessage.ReturnToQueueAsync" /> seeks to the consumed offset.
    /// </summary>
    /// <returns>A task that completes when the seek assertion succeeds.</returns>
    [Fact]
    public async Task ToTransportMessage_ReturnToQueueAsync_ShouldSeekToConsumedOffset()
    {
        TopicPartitionOffset? seekedOffset = null;

        var result = new ConsumeResult<string, byte[]>
        {
            Topic = "orders",
            Partition = 0,
            Offset = 42,
            Message = new Message<string, byte[]>
            {
                Value = Encoding.UTF8.GetBytes("payload"),
                Headers = new Headers()
            }
        };

        var transportMessage = KafkaMessageMapper.ToTransportMessage(
            result,
            "orders",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ => Task.CompletedTask,
                NackAsync = (requeue, _) =>
                {
                    if (requeue)
                    {
                        seekedOffset = result.TopicPartitionOffset;
                    }

                    return Task.CompletedTask;
                }
            },
            redelivered: false);

        await transportMessage.ReturnToQueueAsync().ConfigureAwait(false);

        seekedOffset.Should().Be(result.TopicPartitionOffset);
    }

    /// <summary>
    ///     Verifies <see cref="TransportMessage.DiscardAsync" /> does not seek to the consumed offset.
    /// </summary>
    /// <returns>A task that completes when the seek assertion succeeds.</returns>
    [Fact]
    public async Task ToTransportMessage_DiscardAsync_ShouldNotSeek()
    {
        var seekCount = 0;

        var result = new ConsumeResult<string, byte[]>
        {
            Topic = "orders",
            Partition = 0,
            Offset = 7,
            Message = new Message<string, byte[]>
            {
                Value = Encoding.UTF8.GetBytes("payload"),
                Headers = new Headers()
            }
        };

        var transportMessage = KafkaMessageMapper.ToTransportMessage(
            result,
            "orders",
            new TransportConsumerAckHandlers
            {
                AckAsync = _ => Task.CompletedTask,
                NackAsync = (requeue, _) =>
                {
                    if (requeue)
                    {
                        seekCount++;
                    }

                    return Task.CompletedTask;
                }
            },
            redelivered: false);

        await transportMessage.DiscardAsync().ConfigureAwait(false);

        seekCount.Should().Be(0);
    }
}

/// <summary>
///     Test helpers for reading Kafka header values.
/// </summary>
internal static class KafkaMessageMapperTestsHelpers
{
    /// <summary>
    ///     Reads one UTF-8 encoded Kafka header value when present.
    /// </summary>
    /// <param name="headers">The Kafka headers from a record.</param>
    /// <param name="name">The header name to read.</param>
    /// <returns>The header value, or <see langword="null" /> when absent.</returns>
    internal static string? GetHeader(Headers headers, string name)
    {
        foreach (var header in headers)
        {
            if (header.Key == name && header.GetValueBytes() is { } bytes)
            {
                return Encoding.UTF8.GetString(bytes);
            }
        }

        return null;
    }
}