using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Messaging.Tenancy;

/// <summary>
///     Reports whether tenant metadata comes from the default provider or an application one, for an application that
///     enabled tenancy.
/// </summary>
/// <remarks>
///     <para>
///         Enabling tenancy registers the default <see cref="ITenantMetadataProvider" />, which answers every tenant
///         with unknown metadata. That keeps schedules and policies running, with occurrences read in UTC, but it is a
///         placeholder rather than tenant data: per-tenant zones, cultures, and attributes all stay unknown until the
///         application registers its own provider. A probe turns that into an answer an operator can read before the
///         first schedule observes that every tenant shares one zone.
///     </para>
///     <para>
///         Every resolution happens inside a dispatch scope rather than against the provider the probe was given, so
///         the lookup stays consistent with the other probes under a container that validates scopes.
///     </para>
/// </remarks>
public sealed class TenantMetadataDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The name reported by this probe.
    /// </summary>
    public const string CheckName = "litebus.tenancy.metadata";

    /// <summary>
    ///     Resolves the tenant metadata provider without requiring it, so a missing registration is reported rather
    ///     than thrown.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantMetadataDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to look for a registered tenant metadata provider.</param>
    public TenantMetadataDiagnosticCheck(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => CheckName;

    /// <inheritdoc />
    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var provider = Resolve();

        if (provider is null)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "The tenancy metadata probe cannot be evaluated because no ITenantMetadataProvider is registered. "
                + "Register the probe through UseTenancy rather than by hand.",
                new Dictionary<string, object>
                {
                    ["component"] = "tenancy",
                    ["providerRegistered"] = false
                }));
        }

        if (provider is DefaultTenantMetadataProvider)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Degraded,
                "Tenancy is enabled and tenant metadata resolves through the default provider, so every tenant "
                + "answers unknown: no time zone, no culture, and no attributes. Schedules read occurrences in UTC. "
                + "Implement ITenantMetadataProvider over the application tenant records to resolve per-tenant metadata.",
                new Dictionary<string, object>
                {
                    ["component"] = "tenancy",
                    ["providerRegistered"] = true,
                    ["isDefault"] = true,
                    ["providerType"] = provider.GetType().FullName ?? provider.GetType().Name
                }));
        }

        return Task.FromResult(new DiagnosticResult(
            DiagnosticStatus.Healthy,
            $"Tenancy is enabled and tenant metadata resolves through '{provider.GetType().Name}'.",
            new Dictionary<string, object>
            {
                ["component"] = "tenancy",
                ["providerRegistered"] = true,
                ["isDefault"] = false,
                ["providerType"] = provider.GetType().FullName ?? provider.GetType().Name
            }));
    }

    /// <summary>
    ///     Resolves the tenant metadata provider from a dispatch scope, falling back to the provider the probe was
    ///     given.
    /// </summary>
    /// <returns>The registered provider, or <see langword="null" /> when none is registered.</returns>
    /// <remarks>
    ///     Resolving from a dispatch scope keeps the probe consistent with the other probes under a container that
    ///     validates scopes, even though the provider looked up here is a singleton.
    /// </remarks>
    private ITenantMetadataProvider? Resolve()
    {
        if (_serviceProvider.GetService(typeof(IMessageDispatchScopeFactory)) is not IMessageDispatchScopeFactory factory)
        {
            return _serviceProvider.GetService(typeof(ITenantMetadataProvider)) as ITenantMetadataProvider;
        }

        using var scope = factory.CreateScope();
        return scope.ServiceProvider.GetService(typeof(ITenantMetadataProvider)) as ITenantMetadataProvider;
    }
}
