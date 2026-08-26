namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Reserved <see cref="HandlerPriorityAttribute" /> values for handlers shipped by LiteBus itself.
/// </summary>
/// <remarks>
///     <para>
///         Handlers run in ascending priority order, so a larger number runs later. LiteBus reserves the range at or
///         above <see cref="FrameworkFloor" /> for the handlers it ships, which leaves every value below it to
///         applications. An application handler with no explicit priority sits at zero and therefore runs before all of
///         them.
///     </para>
///     <para>
///         The band exists so that ordering against a LiteBus handler is a documented guarantee rather than something
///         each application rediscovers by experiment. To run after LiteBus writes its audit record, for example, give
///         your handler a priority above <see cref="Observability" />.
///     </para>
/// </remarks>
public static class LiteBusHandlerPriority
{
    /// <summary>
    ///     The priority assigned to handlers that carry no <see cref="HandlerPriorityAttribute" />.
    /// </summary>
    public const int Default = 0;

    /// <summary>
    ///     The lowest priority reserved for handlers shipped by LiteBus.
    /// </summary>
    /// <remarks>
    ///     Application handlers should stay below this value. Everything at or above it may be reordered between LiteBus
    ///     releases.
    /// </remarks>
    public const int FrameworkFloor = 1_000_000;

    /// <summary>
    ///     The priority used by LiteBus handlers that persist state, such as durable storage writes.
    /// </summary>
    public const int Persistence = FrameworkFloor + 100;

    /// <summary>
    ///     The priority used by LiteBus handlers that observe and record, such as the audit record writer.
    /// </summary>
    /// <remarks>
    ///     Observation runs after persistence so that a record describing a change is written once the change itself has
    ///     been committed.
    /// </remarks>
    public const int Observability = FrameworkFloor + 200;
}
