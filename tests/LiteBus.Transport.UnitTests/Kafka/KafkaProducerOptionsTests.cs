using Confluent.Kafka;
using LiteBus.Transport.Kafka;

namespace LiteBus.Transport.UnitTests.Kafka;

/// <summary>
///     Verifies Kafka producer option validation and producer configuration mapping.
/// </summary>
public sealed class KafkaProducerOptionsTests
{
    /// <summary>
    ///     Verifies a non-positive message timeout fails during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithNonPositiveMessageTimeout_ShouldThrow()
    {
        var act = () => new KafkaTransportModule(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            MessageTimeoutMs = 0
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies a non-positive send retry count fails during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithNonPositiveSendRetries_ShouldThrow()
    {
        var act = () => new KafkaTransportModule(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            MessageSendMaxRetries = 0
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies a non-positive socket timeout fails during module construction.
    /// </summary>
    [Fact]
    public void Constructor_WithNonPositiveSocketTimeout_ShouldThrow()
    {
        var act = () => new KafkaTransportModule(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            SocketTimeoutMs = -1
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies each producer option maps only to its own producer configuration field.
    /// </summary>
    [Fact]
    public void BuildProducerConfig_WithAllOptionsSet_ShouldMapEachFieldFromItsOwnOption()
    {
        var config = KafkaProducerOwner.BuildProducerConfig(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092",
            MessageTimeoutMs = 11000,
            SocketTimeoutMs = 12000,
            MessageSendMaxRetries = 7
        });

        config.MessageTimeoutMs.Should().Be(11000);
        config.SocketTimeoutMs.Should().Be(12000);
        config.MessageSendMaxRetries.Should().Be(7);
    }

    /// <summary>
    ///     Verifies unset producer options leave the Confluent defaults in place.
    /// </summary>
    [Fact]
    public void BuildProducerConfig_WithNullOptions_ShouldLeaveConfluentDefaults()
    {
        var defaults = new ProducerConfig { BootstrapServers = "localhost:9092" };
        var config = KafkaProducerOwner.BuildProducerConfig(new KafkaTransportOptions
        {
            BootstrapServers = "localhost:9092"
        });

        config.MessageTimeoutMs.Should().Be(defaults.MessageTimeoutMs);
        config.SocketTimeoutMs.Should().Be(defaults.SocketTimeoutMs);
        config.MessageSendMaxRetries.Should().Be(defaults.MessageSendMaxRetries);
    }
}
