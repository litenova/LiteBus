using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Verifies persisted durable metadata is reconstructed into semantic value objects.
/// </summary>
public sealed class DurableEnvelopeMetadataMapperTests
{
    /// <summary>
    ///     Verifies trace columns select the most specific complete trace shape.
    /// </summary>
    [Fact]
    public void ResolveTrace_ShouldReconstructAvailableTraceShape()
    {
        DurableEnvelopeMetadataMapper.ResolveTrace("correlation", "causation", "trace-parent")
            .Should().Be(new MessageTrace.Distributed("correlation", "causation", "trace-parent"));
        DurableEnvelopeMetadataMapper.ResolveTrace("correlation", "causation", null)
            .Should().Be(new MessageTrace.Workflow("correlation", "causation"));
        DurableEnvelopeMetadataMapper.ResolveTrace("correlation", null, "incomplete-trace")
            .Should().Be(new MessageTrace.Correlated("correlation"));
        DurableEnvelopeMetadataMapper.ResolveTrace(null, "causation", "trace-parent")
            .Should().BeSameAs(MessageTrace.None.Instance);
    }

    /// <summary>
    ///     Verifies tenant columns distinguish isolated and unscoped messages.
    /// </summary>
    [Fact]
    public void ResolveTenant_ShouldReconstructTenantScope()
    {
        DurableEnvelopeMetadataMapper.ResolveTenant("tenant-a")
            .Should().Be(new TenantScope.Isolated("tenant-a"));
        DurableEnvelopeMetadataMapper.ResolveTenant(" ")
            .Should().BeSameAs(TenantScope.Unscoped.Instance);
    }
}
