using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Carries the idempotency key the endpoint filter validated into command mediation settings.
/// </summary>
public static class CommandMediationSettingsExtensions
{
    /// <summary>
    ///     Carries the validated idempotency-key header value from the current endpoint invocation into the command
    ///     mediation the endpoint is about to start.
    /// </summary>
    /// <param name="settings">The mediation settings the endpoint passes to the command mediator.</param>
    /// <param name="context">The current HTTP request context.</param>
    /// <returns>The settings for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Reads the entry <see cref="IdempotencyKeyEndpointExtensions" /> wrote and stores it in the settings
    ///         items under the key <see cref="IdempotencyKeyGuard" /> reads. When the filter is absent, or the header
    ///         was optional and missing, there is no entry and the settings pass through unchanged.
    ///     </para>
    ///     <para>
    ///         A tenant the filter resolved travels alongside the key into the trace tenant entry, so the stored key
    ///         is scoped to the calling tenant. Without it, two tenants sending one key string for one command share
    ///         a store row.
    ///     </para>
    ///     <code><![CDATA[
    /// var settings = new CommandMediationSettings().WithIdempotencyKey(context);
    /// var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
    /// ]]></code>
    /// </remarks>
    public static CommandMediationSettings WithIdempotencyKey(
        this CommandMediationSettings settings,
        HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(IdempotencyKeyItemKeys.HttpContextItem, out var value) && value is string key)
        {
            settings.Items[IdempotencyKeyItemKeys.MediationItem] = new SuppliedIdempotencyKey(key);
        }

        if (context.Items.TryGetValue(IdempotencyKeyItemKeys.TenantHttpContextItem, out var tenantValue) &&
            tenantValue is string tenant &&
            !string.IsNullOrWhiteSpace(tenant))
        {
            settings.Items[MessageTraceContextKeys.TenantId] = tenant;
        }

        return settings;
    }
}
