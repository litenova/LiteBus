using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Describes the tenant a mediation runs in, beyond the identifier.
/// </summary>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="TimeZoneId">The IANA time zone name, or <see langword="null" /> when unknown.</param>
/// <param name="CultureName">The culture name, or <see langword="null" /> when unknown.</param>
/// <param name="Attributes">Additional tenant attributes by name.</param>
/// <remarks>
///     <para>
///         Schedules, checkpoint calendars and policies read this record through
///         <see cref="ITenantMetadataProvider" /> instead of taking fixed constructor values. The data stays in the
///         application: LiteBus ships no tenant store, so each product keeps its own tenant records and exposes them
///         through the provider.
///     </para>
///     <para>
///         The time zone is stored as an IANA name rather than a resolved zone, so stored values survive machine
///         differences and resolve the same on every host.
///     </para>
/// </remarks>
public sealed record TenantMetadata(
    string TenantId,
    string? TimeZoneId,
    string? CultureName,
    IReadOnlyDictionary<string, string> Attributes);
