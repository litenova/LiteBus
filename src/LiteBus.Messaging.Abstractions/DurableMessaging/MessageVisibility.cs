using System;

namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes when a stored durable message becomes eligible for processor leasing.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="Immediate" /> when the message should be due as soon as a processor can lease it.
///         Use <see cref="At" /> for an absolute UTC visibility timestamp.
///         Use <see cref="After" /> for a relative delay resolved against <see cref="TimeProvider" /> at accept or enqueue
///         time.
///     </para>
/// </remarks>
public abstract record MessageVisibility
{
    /// <summary>
    ///     Indicates that the message is due for processing as soon as a processor leases it.
    /// </summary>
    public sealed record Immediate : MessageVisibility
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Immediate" /> class.
        /// </summary>
        private Immediate()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when no deferred visibility is requested.
        /// </summary>
        public static Immediate Instance { get; } = new();
    }

    /// <summary>
    ///     Defers processing until the specified UTC timestamp.
    /// </summary>
    /// <param name="VisibleAfter">The earliest UTC timestamp at which the message may be leased.</param>
    public sealed record At(DateTimeOffset VisibleAfter) : MessageVisibility;

    /// <summary>
    ///     Defers processing until a relative delay elapses from accept or enqueue time.
    /// </summary>
    /// <param name="Delay">The non-negative delay before the message becomes visible.</param>
    public sealed record After(TimeSpan Delay) : MessageVisibility;
}