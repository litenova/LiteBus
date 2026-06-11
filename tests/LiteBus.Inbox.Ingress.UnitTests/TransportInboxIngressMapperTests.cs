using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress.UnitTests;

/// <summary>
///     Verifies transport delivery mapping into inbox acceptance metadata.
/// </summary>
public sealed class TransportInboxIngressMapperTests
{
    /// <summary>
    ///     Verifies inbox envelope headers round-trip through <see cref="TransportMessage" /> to acceptance metadata.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_ShouldRoundTripDispatchHeaders()
    {
        var messageId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = messageId.ToString("D"),
            [TransportHeaders.ContractName] = "orders.commands.ship",
            [TransportHeaders.ContractVersion] = "2",
            [TransportHeaders.CorrelationId] = "corr-1",
            [TransportHeaders.CausationId] = "cause-1",
            [TransportHeaders.TenantId] = "tenant-west",
            [TransportHeaders.TraceContext] = """{"traceparent":"00-abc-def-01"}"""
        };

        var transportMessage = CreateTransportMessage(headers, messageId.ToString("D"), "corr-1");
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage);

        metadata.Identity.Should().Be(new MessageIdentity.Supplied(messageId));

        metadata.Trace.Should().Be(new MessageTrace.Distributed(
            "corr-1",
            "cause-1",
            """{"traceparent":"00-abc-def-01"}"""));

        metadata.Tenant.Should().Be(new TenantScope.Isolated("tenant-west"));

        TransportInboxIngressMapper.GetRequiredHeader(transportMessage, TransportHeaders.ContractName)
            .Should().Be("orders.commands.ship");

        TransportInboxIngressMapper.GetRequiredContractVersion(transportMessage).Should().Be(2);
    }

    /// <summary>
    ///     Verifies optional ingress headers map to inbox acceptance metadata.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_ShouldMapOptionalHeaders()
    {
        var messageId = Guid.NewGuid();
        var visibleAfter = new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = messageId.ToString("D"),
            [TransportHeaders.ContractName] = "orders.commands.ship",
            [TransportHeaders.ContractVersion] = "1",
            [TransportHeaders.IdempotencyKey] = "idem-key-1",
            [TransportHeaders.VisibleAfter] = visibleAfter.ToString("O")
        };

        var transportMessage = CreateTransportMessage(headers);
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage);

        metadata.Identity.Should().Be(new MessageIdentity.Supplied(messageId));
        metadata.Idempotency.Should().Be(new Idempotency.Keyed("idem-key-1"));
        metadata.Visibility.Should().Be(new MessageVisibility.At(visibleAfter));
    }

    /// <summary>
    ///     Creates a transport message with acknowledgement delegates for mapper tests.
    /// </summary>
    /// <param name="headers">The application headers.</param>
    /// <param name="messageId">The optional message identifier property.</param>
    /// <param name="correlationId">The optional correlation identifier property.</param>
    /// <returns>A transport message suitable for mapper assertions.</returns>
    private static TransportMessage CreateTransportMessage(
        IReadOnlyDictionary<string, object?> headers,
        string? messageId = null,
        string? correlationId = null)
    {
        return new TransportMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = headers,
            MessageId = messageId,
            CorrelationId = correlationId,
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }
}