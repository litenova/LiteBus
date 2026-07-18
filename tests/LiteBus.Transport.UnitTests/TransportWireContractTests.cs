using AwesomeAssertions;
using LiteBus.Transport.Abstractions;

namespace LiteBus.Transport.UnitTests;

/// <summary>
///     Documents the canonical LiteBus wire header contract shared across broker adapters.
/// </summary>
public sealed class TransportWireContractTests
{
    /// <summary>
    ///     Verifies canonical transport header names used by dispatch and ingress mappers.
    /// </summary>
    [Fact]
    public void CanonicalHeaders_ShouldUseStableWireNames()
    {
        TransportHeaders.MessageId.Should().Be("litebus-message-id");
        TransportHeaders.ContractName.Should().Be("litebus-contract-name");
        TransportHeaders.ContractVersion.Should().Be("litebus-contract-version");
        TransportHeaders.CorrelationId.Should().Be("correlation-id");
        TransportHeaders.CausationId.Should().Be("causation-id");
        TransportHeaders.TenantId.Should().Be("tenant-id");
        TransportHeaders.TraceContext.Should().Be("litebus-trace-context");
        TransportHeaders.IdempotencyKey.Should().Be("litebus-idempotency-key");
        TransportHeaders.VisibleAfter.Should().Be("litebus-visible-after");
        TransportHeaders.ContentEncoding.Should().Be("litebus-content-encoding");
    }
}
