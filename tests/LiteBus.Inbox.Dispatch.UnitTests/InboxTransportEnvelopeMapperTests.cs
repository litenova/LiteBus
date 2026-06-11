using LiteBus.Inbox.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Dispatch.UnitTests;

/// <summary>
///     Verifies inbox envelope header mapping for transport dispatch.
/// </summary>
public sealed class InboxTransportEnvelopeMapperTests
{
    /// <summary>
    ///     Verifies inbox envelope headers map all metadata fields for transport publish.
    /// </summary>
    [Fact]
    public void BuildHeaders_ShouldMapAllMetadataFields()
    {
        var messageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var envelope = new InboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.commands.ship",
            ContractVersion = 2,
            Payload = """{"orderId":"42"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            CorrelationId = "corr-1",
            CausationId = "cause-1",
            TenantId = "tenant-west",
            TraceContext = """{"traceparent":"00-abc-def-01"}"""
        };

        var headers = InboxTransportEnvelopeMapper.BuildHeaders(envelope);

        headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");
        headers[TransportHeaders.ContractVersion].Should().Be(2);
        headers[TransportHeaders.CorrelationId].Should().Be("corr-1");
        headers[TransportHeaders.CausationId].Should().Be("cause-1");
        headers[TransportHeaders.TenantId].Should().Be("tenant-west");
        headers[TransportHeaders.TraceContext].Should().Be("""{"traceparent":"00-abc-def-01"}""");
    }
}