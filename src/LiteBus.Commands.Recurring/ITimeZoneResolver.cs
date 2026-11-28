using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Resolves a stored time zone name to the zone per-tenant schedules read local times in.
/// </summary>
/// <remarks>
///     <para>
///         Tenant records store zones as IANA names, so stored values survive machine differences and resolve the
///         same on every host. The default resolution is <see cref="SystemTimeZoneResolver" />, which reads through
///         <see cref="TimeZoneInfo.FindSystemTimeZoneById" />; an application whose hosts disagree on zone names
///         replaces it, for example over a mapping library.
///     </para>
///     <para>
///         A name that resolves to <see langword="null" /> reads the tenant's local times as UTC, as the tenant
///         schedule built-ins describe.
///     </para>
/// </remarks>
public interface ITimeZoneResolver
{
    /// <summary>
    ///     Resolves a stored time zone name.
    /// </summary>
    /// <param name="timeZoneId">The stored time zone name.</param>
    /// <returns>The resolved zone, or <see langword="null" /> when the name is unknown.</returns>
    TimeZoneInfo? Resolve(string timeZoneId);
}
