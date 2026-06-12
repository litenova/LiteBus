using AwesomeAssertions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Verifies canonical durable envelope header mapping for transport dispatch.
/// </summary>
public sealed class TransportEnvelopeHeaderMapperTests
{
    /// <summary>
    ///     Verifies all metadata fields map to canonical transport headers including idempotency and visibility.
    /// </summary>
    [Fact]
    public void BuildHeaders_ShouldMapAllMetadataFields()
    {
        var messageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var visibleAfter = new DateTimeOffset(2026, 6, 12, 10, 0, 0, TimeSpan.Zero);

        var headers = TransportEnvelopeHeaderMapper.BuildHeaders(new TransportEnvelopeHeaderSource(
            messageId,
            "orders.commands.ship",
            2,
            "corr-1",
            "cause-1",
            "tenant-west",
            """{"traceparent":"00-abc-def-01"}""",
            "idem-key-1",
            visibleAfter));

        headers[TransportHeaders.MessageId].Should().Be(messageId.ToString("D"));
        headers[TransportHeaders.ContractName].Should().Be("orders.commands.ship");
        headers[TransportHeaders.ContractVersion].Should().Be(2);
        headers[TransportHeaders.CorrelationId].Should().Be("corr-1");
        headers[TransportHeaders.CausationId].Should().Be("cause-1");
        headers[TransportHeaders.TenantId].Should().Be("tenant-west");
        headers[TransportHeaders.TraceContext].Should().Be("""{"traceparent":"00-abc-def-01"}""");
        headers[TransportHeaders.IdempotencyKey].Should().Be("idem-key-1");
        headers[TransportHeaders.VisibleAfter].Should().Be(visibleAfter.ToString("O"));
    }
}
