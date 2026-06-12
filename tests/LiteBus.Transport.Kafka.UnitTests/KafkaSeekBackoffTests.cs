using AwesomeAssertions;
using Confluent.Kafka;

namespace LiteBus.Transport.Kafka.UnitTests;

/// <summary>
///     Verifies seek backoff computation for repeated Kafka ingress failures.
/// </summary>
public sealed class KafkaSeekBackoffTests
{
    /// <summary>
    ///     Verifies repeated failures at the same offset increase the backoff delay exponentially.
    /// </summary>
    [Fact]
    public void RecordSeek_repeatedFailures_ShouldIncreaseBackoff()
    {
        var backoff = new KafkaSeekBackoff(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            SeekFailureBackoffInitial = TimeSpan.FromMilliseconds(100),
            SeekFailureBackoffMax = TimeSpan.FromSeconds(5),
            SeekFailureBackoffMultiplier = 2.0
        });

        var offset = new TopicPartitionOffset("orders", 0, 10);
        var first = backoff.RecordSeek(offset);
        var second = backoff.RecordSeek(offset);

        first.Should().Be(TimeSpan.FromMilliseconds(100));
        second.Should().Be(TimeSpan.FromMilliseconds(200));
    }

    /// <summary>
    ///     Verifies seeked offsets are reported as redeliveries until committed.
    /// </summary>
    [Fact]
    public void IsRedelivery_afterSeek_ShouldBeTrueUntilCommit()
    {
        var backoff = new KafkaSeekBackoff(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092"
        });

        var offset = new TopicPartitionOffset("orders", 0, 10);

        backoff.IsRedelivery(offset).Should().BeFalse();

        backoff.RecordSeek(offset);
        backoff.IsRedelivery(offset).Should().BeTrue();

        backoff.RecordCommit(offset);
        backoff.IsRedelivery(offset).Should().BeFalse();
    }

    /// <summary>
    ///     Verifies committing an offset clears failure tracking for that offset.
    /// </summary>
    [Fact]
    public void RecordCommit_afterSeek_ShouldResetBackoff()
    {
        var backoff = new KafkaSeekBackoff(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            SeekFailureBackoffInitial = TimeSpan.FromMilliseconds(100),
            SeekFailureBackoffMultiplier = 2.0
        });

        var offset = new TopicPartitionOffset("orders", 0, 10);
        backoff.RecordSeek(offset);
        backoff.RecordCommit(offset);

        var afterCommit = backoff.RecordSeek(offset);
        afterCommit.Should().Be(TimeSpan.FromMilliseconds(100));
    }
}