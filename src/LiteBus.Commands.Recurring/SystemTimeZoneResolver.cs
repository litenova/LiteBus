using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Resolves stored time zone names through <see cref="TimeZoneInfo.FindSystemTimeZoneById" />.
/// </summary>
/// <remarks>
///     A name the host does not know resolves to <see langword="null" />, so the tenant's local times read as UTC.
/// </remarks>
public sealed class SystemTimeZoneResolver : ITimeZoneResolver
{
    /// <summary>
    ///     Gets the shared system time zone resolver.
    /// </summary>
    public static SystemTimeZoneResolver Instance { get; } = new();

    /// <inheritdoc />
    public TimeZoneInfo? Resolve(string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}
