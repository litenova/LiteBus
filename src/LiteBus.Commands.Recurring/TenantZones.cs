using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Resolves per-tenant schedule zones from tenant metadata.
/// </summary>
/// <remarks>
///     <para>
///         The zone source behind per-tenant schedules: the returned function reads one tenant's record through
///         <see cref="ITenantMetadataProvider" />, resolves its <c>TimeZoneId</c> through
///         <see cref="ITimeZoneResolver" />, and answers the <c>zoneOf</c> parameter of the tenant schedule
///         built-ins. A tenant with no record, no zone, or an unresolvable zone resolves to
///         <see langword="null" /> and reads its local times as UTC.
///     </para>
///     <para>
///         The provider instance is captured when the function is built, so it must be safe to call for the life
///         of the host. The loop calls the function on every wait, so a provider that reads over the network
///         should cache. An unknown zone is reported once per tenant through the supplied logger.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// host.TenantSchedule = RecurringSchedules
///     .DailyAt(new TimeOnly(0, 0), TenantZones.FromMetadata(provider))
///     .AsRecurringSchedule();
/// ]]></code>
/// </example>
public static class TenantZones
{
    /// <summary>
    ///     The delegate that writes the unknown-zone event.
    /// </summary>
    private static readonly Action<ILogger, string, string, Exception?> ZoneUnknownMessage =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2121, nameof(ZoneUnknown)),
            "Tenant '{TenantId}' {Reason}; its schedule reads local times as UTC.");

    /// <summary>
    ///     Creates the zone function that reads each tenant's zone from a metadata provider.
    /// </summary>
    /// <param name="provider">The provider the function reads tenant records through.</param>
    /// <param name="resolver">Resolves stored zone names, or <see langword="null" /> for <see cref="SystemTimeZoneResolver" />.</param>
    /// <param name="logger">Receives the unknown-zone event once per tenant, or <see langword="null" /> to stay silent.</param>
    /// <returns>The function the tenant schedule built-ins take as <c>zoneOf</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider" /> is <see langword="null" />.</exception>
    public static Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> FromMetadata(
        ITenantMetadataProvider provider,
        ITimeZoneResolver? resolver = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var zoneResolver = resolver ?? SystemTimeZoneResolver.Instance;
        var warned = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        return async (tenantId, cancellationToken) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

            var found = await provider.GetManyAsync([tenantId], cancellationToken).ConfigureAwait(false);

            if (!found.TryGetValue(tenantId, out var metadata))
            {
                WarnOnce(logger, warned, tenantId, "has no metadata");
                return null;
            }

            if (string.IsNullOrWhiteSpace(metadata.TimeZoneId))
            {
                WarnOnce(logger, warned, tenantId, "carries no time zone");
                return null;
            }

            var zone = zoneResolver.Resolve(metadata.TimeZoneId);

            if (zone is null)
            {
                WarnOnce(logger, warned, tenantId, $"reports the unknown zone '{metadata.TimeZoneId}'");
                return null;
            }

            return zone;
        };
    }

    /// <summary>
    ///     Creates the zone function that reads each tenant's zone from the provider resolved from services.
    /// </summary>
    /// <param name="services">The service provider the metadata provider and the zone resolver are resolved from.</param>
    /// <param name="logger">Receives the unknown-zone event once per tenant, or <see langword="null" /> to stay silent.</param>
    /// <returns>The function the tenant schedule built-ins take as <c>zoneOf</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services" /> is <see langword="null" />.</exception>
    /// <exception cref="LiteBusDependencyResolutionException">No <see cref="ITenantMetadataProvider" /> is registered.</exception>
    /// <remarks>
    ///     Both services are resolved once when the function is built. The zone resolver is optional and falls back
    ///     to <see cref="SystemTimeZoneResolver" />.
    /// </remarks>
    public static Func<string, CancellationToken, ValueTask<TimeZoneInfo?>> FromMetadata(
        IServiceProvider services,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var provider = (ITenantMetadataProvider?) services.GetService(typeof(ITenantMetadataProvider));

        if (provider is null)
        {
            throw new LiteBusDependencyResolutionException(typeof(ITenantMetadataProvider));
        }

        var resolver = (ITimeZoneResolver?) services.GetService(typeof(ITimeZoneResolver));

        return FromMetadata(provider, resolver, logger);
    }

    /// <summary>
    ///     Writes the unknown-zone event the first time one tenant's zone cannot be resolved.
    /// </summary>
    /// <param name="logger">The logger receiving the event, or <see langword="null" /> to stay silent.</param>
    /// <param name="warned">The tenants already reported.</param>
    /// <param name="tenantId">The tenant whose zone cannot be resolved.</param>
    /// <param name="reason">Why the zone cannot be resolved.</param>
    private static void WarnOnce(ILogger? logger, ConcurrentDictionary<string, byte> warned, string tenantId, string reason)
    {
        if (logger is null || !warned.TryAdd(tenantId, 0))
        {
            return;
        }

        ZoneUnknown(logger, tenantId, reason);
    }

    /// <summary>
    ///     Writes the unknown-zone event.
    /// </summary>
    /// <param name="logger">The logger receiving the event.</param>
    /// <param name="tenantId">The tenant whose zone cannot be resolved.</param>
    /// <param name="reason">Why the zone cannot be resolved.</param>
    private static void ZoneUnknown(ILogger logger, string tenantId, string reason)
    {
        ZoneUnknownMessage(logger, tenantId, reason, null);
    }
}
