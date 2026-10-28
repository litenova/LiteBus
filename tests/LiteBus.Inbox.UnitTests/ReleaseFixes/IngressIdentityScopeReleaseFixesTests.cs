using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies broker-derived inbox identity is scoped to the ingress destination.
/// </summary>
public sealed class IngressIdentityScopeReleaseFixesTests
{
    /// <summary>
    ///     Verifies the same broker delivery id on two destinations maps to two identities.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenSameBrokerIdOnTwoDestinations_ShouldProduceTwoIdentities()
    {
        var mappingOptions = new TransportInboxIngressMappingOptions(
            RequireStableIdentity: true,
            TrustApplicationHeaders: false);

        var first = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage("queue-a", "delivery-1001"),
            mappingOptions);
        var second = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage("queue-b", "delivery-1001"),
            mappingOptions);

        first.Identity.Should().NotBe(second.Identity);
    }

    /// <summary>
    ///     Verifies broker redelivery on one destination keeps a stable identity.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenSameBrokerIdOnOneDestination_ShouldKeepStableIdentity()
    {
        var mappingOptions = new TransportInboxIngressMappingOptions(
            RequireStableIdentity: true,
            TrustApplicationHeaders: false);

        var first = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage("queue-a", "delivery-1001"),
            mappingOptions);
        var second = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage("queue-a", "delivery-1001"),
            mappingOptions);

        second.Identity.Should().Be(first.Identity);
    }

    /// <summary>
    ///     Verifies GUID broker delivery ids are still used verbatim as inbox identity.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenBrokerIdIsGuid_ShouldUseVerbatimIdentity()
    {
        var brokerId = Guid.NewGuid().ToString("D");
        var mappingOptions = new TransportInboxIngressMappingOptions(
            RequireStableIdentity: true,
            TrustApplicationHeaders: false);

        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(
            CreateMessage("queue-a", brokerId),
            mappingOptions);

        metadata.Identity.Should().Be(new MessageIdentity.Supplied(Guid.Parse(brokerId)));
    }

    /// <summary>
    ///     Creates a transport message with the supplied destination and broker delivery id.
    /// </summary>
    /// <param name="destination">The ingress destination address.</param>
    /// <param name="brokerDeliveryId">The broker delivery identifier.</param>
    /// <returns>A transport message suitable for mapper assertions.</returns>
    private static TransportMessage CreateMessage(string destination, string brokerDeliveryId)
    {
        return new TransportMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = new Dictionary<string, object?>(StringComparer.Ordinal),
            MessageId = brokerDeliveryId,
            Destination = destination,
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }
}
