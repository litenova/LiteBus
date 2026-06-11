using LiteBus.Outbox.Abstractions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Outbox.Dispatch.UnitTests;

/// <summary>
///     Verifies outbox envelope header mapping for transport dispatch.
/// </summary>
public sealed class OutboxTransportEnvelopeMapperTests
{
    /// <summary>
    ///     Verifies outbox envelope headers preserve metadata on the wire.
    /// </summary>
    [Fact]
    public void BuildHeaders_ShouldMapAllMetadataFields()
    {
        var messageId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

        var envelope = new OutboxEnvelope
        {
            Id = messageId,
            ContractName = "orders.order-submitted",
            ContractVersion = 1,
            Payload = """{"orderId":"99"}""",
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OutboxStatus.Pending,
            AttemptCount = 0,
            CorrelationId = "corr-outbox",
            CausationId = "cause-outbox",
            TenantId = "tenant-east",
            TraceContext = """{"traceparent":"00-outbox-trace-01"}"""
        };

        var headers = OutboxTransportEnvelopeMapper.BuildHeaders(envelope);

        headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be("orders.order-submitted");
        headers[TransportHeaders.ContractVersion].Should().Be(1);
        headers[TransportHeaders.CorrelationId].Should().Be("corr-outbox");
        headers[TransportHeaders.CausationId].Should().Be("cause-outbox");
        headers[TransportHeaders.TenantId].Should().Be("tenant-east");
        headers[TransportHeaders.TraceContext].Should().Be("""{"traceparent":"00-outbox-trace-01"}""");
    }
}