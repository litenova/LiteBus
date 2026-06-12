using LiteBus.Inbox.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Inbox.Ingress.UnitTests;

/// <summary>
///     Verifies transport delivery mapping into inbox acceptance metadata.
/// </summary>
public sealed class TransportInboxIngressMapperTests
{
    private static readonly TransportInboxIngressMappingOptions DefaultMapping = new();

    private static readonly TransportInboxIngressMappingOptions TrustedHeadersMapping = new(
        RequireStableIdentity: true,
        TrustApplicationHeaders: true);

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
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage, TrustedHeadersMapping);

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
    ///     Verifies optional ingress headers map to inbox acceptance metadata when application headers are trusted.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_ShouldMapOptionalHeadersWhenTrusted()
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
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage, TrustedHeadersMapping);

        metadata.Identity.Should().Be(new MessageIdentity.Supplied(messageId));
        metadata.Idempotency.Should().Be(new Idempotency.Keyed("idem-key-1"));
        metadata.Visibility.Should().Be(new MessageVisibility.At(visibleAfter));
    }

    /// <summary>
    ///     Verifies relative visibility delay headers map to deferred inbox acceptance.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_ShouldMapVisibleAfterDelayHeader()
    {
        var messageId = Guid.NewGuid();
        var delay = TimeSpan.FromMinutes(15);

        var headers = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [TransportHeaders.MessageId] = messageId.ToString("D"),
            [TransportHeaders.ContractName] = "orders.commands.ship",
            [TransportHeaders.ContractVersion] = "1",
            [TransportHeaders.VisibleAfterDelay] = delay.ToString("c")
        };

        var transportMessage = CreateTransportMessage(headers);
        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage, TrustedHeadersMapping);

        metadata.Visibility.Should().Be(new MessageVisibility.After(delay));
    }

    /// <summary>
    ///     Verifies broker delivery ids map to broker-scoped idempotency by default.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_ShouldDefaultToBrokerScopedIdempotency()
    {
        var brokerMessageId = "broker-delivery-42";

        var transportMessage = CreateTransportMessage(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.MessageId] = brokerMessageId,
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1",
                [TransportHeaders.IdempotencyKey] = "untrusted-key",
                [TransportHeaders.TenantId] = "tenant-east"
            },
            destination: "commands.inbox");

        var metadata = TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage, DefaultMapping);

        metadata.Idempotency.Should().Be(new Idempotency.Keyed("ingress:commands.inbox:broker-delivery-42"));
        metadata.Tenant.Should().Be(TenantScope.Unscoped.Instance);
    }

    /// <summary>
    ///     Verifies missing broker delivery ids fail closed when stable identity is required.
    /// </summary>
    [Fact]
    public void ToInboxAcceptMetadata_WhenBrokerIdMissingAndRequired_ShouldThrow()
    {
        var transportMessage = CreateTransportMessage(
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TransportHeaders.ContractName] = "orders.commands.ship",
                [TransportHeaders.ContractVersion] = "1"
            });

        var act = () => TransportInboxIngressMapper.ToInboxAcceptMetadata(transportMessage, DefaultMapping);

        act.Should().Throw<InboxIngressException>()
            .WithMessage("*stable broker delivery id*");
    }

    /// <summary>
    ///     Creates a transport message with acknowledgement delegates for mapper tests.
    /// </summary>
    /// <param name="headers">The application headers.</param>
    /// <param name="messageId">The optional message identifier property.</param>
    /// <param name="correlationId">The optional correlation identifier property.</param>
    /// <param name="destination">The optional destination address.</param>
    /// <returns>A transport message suitable for mapper assertions.</returns>
    private static TransportMessage CreateTransportMessage(
        IReadOnlyDictionary<string, object?> headers,
        string? messageId = null,
        string? correlationId = null,
        string? destination = null)
    {
        return new TransportMessage
        {
            Body = ReadOnlyMemory<byte>.Empty,
            Headers = headers,
            MessageId = messageId,
            CorrelationId = correlationId,
            Destination = destination,
            AckAsync = _ => Task.CompletedTask,
            NackAsync = (_, _) => Task.CompletedTask
        };
    }
}
