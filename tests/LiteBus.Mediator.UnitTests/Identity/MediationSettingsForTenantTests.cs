using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Identity;

/// <summary>
///     Verifies that the mediation settings tenant factories carry the tenant identity into the settings.
/// </summary>
public sealed class MediationSettingsForTenantTests : LiteBusTestBase
{
    [Fact]
    public void Command_for_tenant_carries_the_tenant_identity()
    {
        var settings = CommandMediationSettings.ForTenant("tenant-a");

        settings.Identity.Should().NotBeNull();
        settings.Identity.Should().Be(ExecutionIdentity.ForTenant("tenant-a"));
        settings.Identity!.TenantId.Should().Be("tenant-a");
        settings.Identity.HasTenant.Should().BeTrue();
        settings.Identity.Principal.Should().BeNull();
    }

    [Fact]
    public void Query_for_tenant_carries_the_tenant_identity()
    {
        var settings = QueryMediationSettings.ForTenant("tenant-a");

        settings.Identity.Should().NotBeNull();
        settings.Identity.Should().Be(ExecutionIdentity.ForTenant("tenant-a"));
        settings.Identity!.TenantId.Should().Be("tenant-a");
        settings.Identity.HasTenant.Should().BeTrue();
        settings.Identity.Principal.Should().BeNull();
    }

    [Fact]
    public void Event_for_tenant_carries_the_tenant_identity()
    {
        var settings = EventMediationSettings.ForTenant("tenant-a");

        settings.Identity.Should().NotBeNull();
        settings.Identity.Should().Be(ExecutionIdentity.ForTenant("tenant-a"));
        settings.Identity!.TenantId.Should().Be("tenant-a");
        settings.Identity.HasTenant.Should().BeTrue();
        settings.Identity.Principal.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Command_for_blank_tenant_is_anonymous(string? tenantId)
    {
        CommandMediationSettings.ForTenant(tenantId).Identity.Should().Be(ExecutionIdentity.Anonymous);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Query_for_blank_tenant_is_anonymous(string? tenantId)
    {
        QueryMediationSettings.ForTenant(tenantId).Identity.Should().Be(ExecutionIdentity.Anonymous);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Event_for_blank_tenant_is_anonymous(string? tenantId)
    {
        EventMediationSettings.ForTenant(tenantId).Identity.Should().Be(ExecutionIdentity.Anonymous);
    }
}
