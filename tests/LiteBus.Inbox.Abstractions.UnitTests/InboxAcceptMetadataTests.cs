using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.Abstractions.UnitTests;

/// <summary>
///     Verifies inbox acceptance metadata defaults align with outbox enqueue metadata.
/// </summary>
public sealed class InboxAcceptMetadataTests
{
    /// <summary>
    ///     Verifies immediate metadata exposes explicit default variants.
    /// </summary>
    [Fact]
    public void Immediate_should_match_outbox_default_shape()
    {
        var metadata = InboxAcceptMetadata.Immediate;

        metadata.Identity.Should().Be(MessageIdentity.Generated.Instance);
        metadata.Idempotency.Should().Be(Idempotency.None.Instance);
        metadata.Visibility.Should().Be(MessageVisibility.Immediate.Instance);
        metadata.Trace.Should().Be(MessageTrace.None.Instance);
        metadata.Tenant.Should().Be(TenantScope.Unscoped.Instance);
    }

    /// <summary>
    ///     Verifies metadata supports record composition through with expressions.
    /// </summary>
    [Fact]
    public void Immediate_with_should_override_single_concern()
    {
        var metadata = InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed("payment:42")
        };

        metadata.Idempotency.Should().Be(new Idempotency.Keyed("payment:42"));
        metadata.Identity.Should().Be(MessageIdentity.Generated.Instance);
    }
}
