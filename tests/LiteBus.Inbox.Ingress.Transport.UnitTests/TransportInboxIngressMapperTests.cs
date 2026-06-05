using System.Text;
using AwesomeAssertions;
using LiteBus.Inbox.Ingress.Transport;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress.Transport.UnitTests;

/// <summary>
///     Verifies transport delivery mapping into inbox acceptance metadata.
/// </summary>
public sealed class TransportInboxIngressMapperTests
{
    /// <summary>
    ///     Verifies inbox envelope headers round-trip through <see cref="TransportMessage" /> to inbox options.
    /// </summary>
    [Fact]
    public void ToInboxOptions_ShouldRoundTripDispatchHeaders()
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
        var options = TransportInboxIngressMapper.ToInboxOptions(transportMessage);

        options.Id.Should().Be(messageId);
        options.CorrelationId.Should().Be("corr-1");
        options.CausationId.Should().Be("cause-1");
        options.TenantId.Should().Be("tenant-west");
        options.TraceContext.Should().Be("""{"traceparent":"00-abc-def-01"}""");
        TransportInboxIngressMapper.GetRequiredHeader(transportMessage, TransportHeaders.ContractName)
            .Should().Be("orders.commands.ship");
        TransportInboxIngressMapper.GetRequiredContractVersion(transportMessage).Should().Be(2);
    }

    /// <summary>
    ///     Verifies optional ingress headers map to inbox options.
    /// </summary>
    [Fact]
    public void ToInboxOptions_ShouldMapOptionalHeaders()
    {
        var visibleAfter = new DateTimeOffset(2026, 6, 5, 12, 0, 0, TimeSpan.Zero);
        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = Guid.NewGuid().ToString("D"),
            [TransportHeaders.ContractName] = "orders.commands.ship",
            [TransportHeaders.ContractVersion] = "1",
            [TransportHeaders.IdempotencyKey] = "idem-key-1",
            [TransportHeaders.VisibleAfter] = visibleAfter.ToString("O")
        };

        var transportMessage = CreateTransportMessage(headers);
        var options = TransportInboxIngressMapper.ToInboxOptions(transportMessage);

        options.IdempotencyKey.Should().Be("idem-key-1");
        options.VisibleAfter.Should().Be(visibleAfter);
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
        string? correlationId = null) =>
        new()
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = headers,
            MessageId = messageId,
            CorrelationId = correlationId,
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
}
