using System;

namespace LiteBus.Runtime.Scheduling;

/// <summary>
///     Resolves calendar dates and local times to instants for the daily and weekly schedules.
/// </summary>
internal static class ScheduleDates
{
    /// <summary>
    ///     Gets the calendar date the supplied instant falls on in the supplied zone.
    /// </summary>
    /// <param name="value">The instant to place.</param>
    /// <param name="zone">The zone whose calendar days are used.</param>
    /// <returns>The midnight starting that calendar day, with an unspecified kind.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="zone" /> is <see langword="null" />.</exception>
    internal static DateTime ToZonedDate(DateTimeOffset value, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var zoned = TimeZoneInfo.ConvertTime(value, zone);

        return new DateTime(zoned.Year, zoned.Month, zoned.Day, 0, 0, 0, DateTimeKind.Unspecified);
    }

    /// <summary>
    ///     Resolves a calendar date and a local time to the instant it denotes in the supplied zone.
    /// </summary>
    /// <param name="zone">The zone the local time is read in.</param>
    /// <param name="date">The calendar date.</param>
    /// <param name="time">The local time on that date.</param>
    /// <returns>The instant the local date and time denote.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="zone" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     A local time inside a daylight-saving gap denotes no instant, so it resolves forward to the next valid
    ///     instant. A local time inside a fall-back overlap denotes two instants, so it resolves to the earlier one.
    /// </remarks>
    internal static DateTimeOffset ResolveInstant(TimeZoneInfo zone, DateTime date, TimeOnly time)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = new DateTime(date.Year, date.Month, date.Day).Add(time.ToTimeSpan());

        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (!zone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        var offsets = zone.GetAmbiguousTimeOffsets(local);
        var offset = offsets[0];

        foreach (var candidate in offsets)
        {
            if (candidate > offset)
            {
                offset = candidate;
            }
        }

        // The instant is the local time minus the offset, so the largest offset is the earliest occurrence.
        return new DateTimeOffset(local, offset);
    }
}
