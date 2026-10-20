using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;

namespace LiteBus.Transport.UnitTests.Amqp;

/// <summary>
///     Verifies the mandatory flag defaults to requiring a route on both publish request types.
/// </summary>
public sealed class AmqpPublishRequestDefaultsTests
{
    /// <summary>
    ///     Verifies the neutral request requires routing so unroutable messages fail instead of dropping.
    /// </summary>
    [Fact]
    public void TransportPublishRequest_Mandatory_ShouldDefaultToTrue()
    {
        var request = new TransportPublishRequest
        {
            Destination = "orders.events",
            Body = ReadOnlyMemory<byte>.Empty
        };

        request.Mandatory.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies the AMQP request default matches the neutral request default.
    /// </summary>
    [Fact]
    public void AmqpPublishRequest_Mandatory_ShouldDefaultToTrue()
    {
        var request = new AmqpPublishRequest
        {
            Exchange = "orders.events",
            RoutingKey = "orders.created",
            Body = ReadOnlyMemory<byte>.Empty
        };

        request.Mandatory.Should().BeTrue();
    }
}
