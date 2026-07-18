using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Inbox.UnitTests.Abstractions;

/// <summary>
///     Verifies inbox acceptance item factories and metadata composition.
/// </summary>
public sealed class InboxAcceptItemTests
{
    /// <summary>
    ///     Verifies untyped items carry explicit message types for heterogeneous batches.
    /// </summary>
    [Fact]
    public void From_with_message_type_should_set_contract_lookup_type()
    {
        var item = InboxAcceptItem.From(new object(), typeof(string));

        item.MessageType.Should().Be(typeof(string));
        item.Metadata.Should().BeEquivalentTo(InboxAcceptMetadata.Immediate);
    }

    /// <summary>
    ///     Verifies trace helper factories compose immediate metadata.
    /// </summary>
    [Fact]
    public void WithTrace_should_apply_trace_metadata()
    {
        var item = InboxAcceptItem<string>.WithTrace("payload", new MessageTrace.Correlated("order-1"));

        item.Metadata.Trace.Should().Be(new MessageTrace.Correlated("order-1"));
    }

    /// <summary>
    ///     Verifies tenant helper factories compose immediate metadata.
    /// </summary>
    [Fact]
    public void WithTenant_should_apply_tenant_metadata()
    {
        var item = InboxAcceptItem<string>.WithTenant("payload", "tenant-west");

        item.Metadata.Tenant.Should().Be(new TenantScope.Isolated("tenant-west"));
    }

    /// <summary>
    ///     Verifies correlation helper factories compose immediate metadata.
    /// </summary>
    [Fact]
    public void WithCorrelation_should_apply_correlated_trace()
    {
        var item = InboxAcceptItem<string>.WithCorrelation("payload", "corr-42");

        item.Metadata.Trace.Should().Be(new MessageTrace.Correlated("corr-42"));
    }
}
