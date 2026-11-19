using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Tenancy;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Tenancy;

/// <summary>
///     Verifies that the default tenant metadata provider answers unknown metadata and that the tenancy probe reports
///     whether metadata comes from the default or an application provider.
/// </summary>
[Collection("Sequential")]
public sealed class TenantMetadataTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering tenancy with the given messaging configuration.
    /// </summary>
    /// <param name="configureMessaging">Configures the messaging module.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(Action<MessageModuleBuilder> configureMessaging)
    {
        return new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(configureMessaging))
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Verifies that the default provider answers every requested tenant with unknown metadata.
    /// </summary>
    [Fact]
    public async Task The_default_provider_answers_every_tenant_with_unknown_metadata()
    {
        var provider = BuildProvider(messaging => messaging.UseTenancy());
        var metadata = provider.GetRequiredService<ITenantMetadataProvider>();

        metadata.Should().BeOfType<DefaultTenantMetadataProvider>();

        var result = await metadata
            .GetManyAsync(["workspace-a", "workspace-b"], CancellationToken.None).ConfigureAwait(false);

        result.Should().HaveCount(2);

        var record = result["workspace-a"];
        record.TenantId.Should().Be("workspace-a");
        record.TimeZoneId.Should().BeNull();
        record.CultureName.Should().BeNull();
        record.Attributes.Should().BeEmpty();

        result["workspace-b"].TenantId.Should().Be("workspace-b");
        result["workspace-b"].TimeZoneId.Should().BeNull();
    }

    /// <summary>
    ///     Verifies that the default provider answers an empty request with no records.
    /// </summary>
    [Fact]
    public async Task The_default_provider_answers_an_empty_request_with_no_records()
    {
        var provider = BuildProvider(messaging => messaging.UseTenancy());
        var metadata = provider.GetRequiredService<ITenantMetadataProvider>();

        var result = await metadata
            .GetManyAsync([], CancellationToken.None).ConfigureAwait(false);

        result.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that the probe reports degraded metadata when the default provider answers.
    /// </summary>
    [Fact]
    public async Task The_probe_reports_degraded_when_the_default_provider_answers()
    {
        var provider = BuildProvider(messaging => messaging.UseTenancy());
        var check = new TenantMetadataDiagnosticCheck(provider);

        check.Name.Should().Be(TenantMetadataDiagnosticCheck.CheckName);
        TenantMetadataDiagnosticCheck.CheckName.Should().Be("litebus.tenancy.metadata");

        var result = await check.CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Degraded);
        result.Description.Should().Contain("unknown");
        result.Data!["providerRegistered"].Should().Be(true);
        result.Data["isDefault"].Should().Be(true);
        result.Data["providerType"].Should().Be(typeof(DefaultTenantMetadataProvider).FullName);
    }

    /// <summary>
    ///     Verifies that the probe reports healthy metadata when an application provider is registered.
    /// </summary>
    [Fact]
    public async Task The_probe_reports_healthy_when_an_application_provider_is_registered()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging => messaging.UseTenancy()));
        services.AddSingleton<ITenantMetadataProvider>(new FixedTenantMetadataProvider());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ITenantMetadataProvider>().Should().BeOfType<FixedTenantMetadataProvider>();

        var result = await new TenantMetadataDiagnosticCheck(provider)
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Healthy);
        result.Data!["providerRegistered"].Should().Be(true);
        result.Data["isDefault"].Should().Be(false);
        result.Data["providerType"].Should().Be(typeof(FixedTenantMetadataProvider).FullName);
    }

    /// <summary>
    ///     Verifies that the probe reports unhealthy metadata when no provider is registered.
    /// </summary>
    [Fact]
    public async Task The_probe_reports_unhealthy_when_no_provider_is_registered()
    {
        var provider = BuildProvider(_ => { });

        var result = await new TenantMetadataDiagnosticCheck(provider)
            .CheckAsync().ConfigureAwait(false);

        result.Status.Should().Be(DiagnosticStatus.Unhealthy);
        result.Data!["providerRegistered"].Should().Be(false);
    }
}

/// <summary>
///     Answers every tenant with fixed metadata, standing in for an application tenant store.
/// </summary>
internal sealed class FixedTenantMetadataProvider : ITenantMetadataProvider
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
        IReadOnlyList<string> tenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        Dictionary<string, TenantMetadata> result = new(StringComparer.Ordinal);

        foreach (var tenantId in tenantIds)
        {
            Dictionary<string, string> attributes = new(StringComparer.Ordinal) { ["plan"] = "team" };

            result[tenantId] = new TenantMetadata(tenantId, "America/Chicago", "en-US", attributes);
        }

        return new ValueTask<IReadOnlyDictionary<string, TenantMetadata>>(result);
    }
}
