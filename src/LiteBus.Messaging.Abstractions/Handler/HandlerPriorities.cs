namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Named <see cref="HandlerPriorityAttribute" /> values that fix the order of application handlers against the
///     handlers LiteBus ships.
/// </summary>
/// <remarks>
///     <para>
///         Handlers run in ascending priority order, so a larger number runs later. LiteBus reserves the window from
///         <see cref="ReservedFloor" /> up to but not including <see cref="ReservedCeiling" /> for the handlers it
///         ships. Applications own everything below the floor and everything at or above the ceiling. An application
///         handler with no explicit priority sits at <see cref="Default" /> and therefore runs before every LiteBus
///         handler.
///     </para>
///     <para>
///         The reserved window is a window rather than an open-ended range because some application handlers have to
///         run after LiteBus writes. A unit of work that has to flush an audit record cannot commit before the audit
///         writer produces it, so it needs a position on the far side of the reserved values that will not be reordered
///         out from under it. That position is <see cref="UnitOfWork" />.
///     </para>
///     <para>
///         Only <see cref="Persistence" /> and <see cref="Observability" /> may be reordered between LiteBus releases,
///         and only relative to each other. The floor and the ceiling are stable, so the two application bands they
///         delimit are stable too.
///     </para>
/// </remarks>
public static class HandlerPriorities
{
    /// <summary>
    ///     The priority assigned to handlers that carry no <see cref="HandlerPriorityAttribute" />.
    /// </summary>
    public const int Default = 0;

    /// <summary>
    ///     The lowest priority reserved for handlers shipped by LiteBus.
    /// </summary>
    /// <remarks>
    ///     Application handlers that should run before every LiteBus handler stay below this value, which is where an
    ///     unannotated handler already sits. An application handler that has to run after them belongs at or above
    ///     <see cref="ReservedCeiling" /> instead of inside the window.
    /// </remarks>
    public const int ReservedFloor = 1_000_000;

    /// <summary>
    ///     The priority used by LiteBus handlers that persist state, such as durable storage writes.
    /// </summary>
    public const int Persistence = ReservedFloor + 100;

    /// <summary>
    ///     The priority used by LiteBus handlers that observe and record, such as the audit record writer.
    /// </summary>
    /// <remarks>
    ///     Observation runs after persistence so that LiteBus has finished its own durable writes before it records
    ///     that they happened. It says nothing about the application's transaction, which commits at
    ///     <see cref="UnitOfWork" />, after this.
    /// </remarks>
    public const int Observability = ReservedFloor + 200;

    /// <summary>
    ///     The first priority above the range reserved for handlers shipped by LiteBus.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An application handler at or above this value runs after every LiteBus handler for the same role, and
    ///         the guarantee holds across releases. Values between <see cref="ReservedFloor" /> and this ceiling belong
    ///         to LiteBus.
    ///     </para>
    ///     <para>
    ///         This is a boundary marker and not a position. Nothing LiteBus ships sits here, and nothing an
    ///         application writes should either: the band from this value up to <see cref="UnitOfWork" /> is where
    ///         application infrastructure that has to run after LiteBus and before the commit belongs.
    ///     </para>
    /// </remarks>
    public const int ReservedCeiling = 2_000_000;

    /// <summary>
    ///     The priority at which an application commits its unit of work.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A completion handler at this priority runs after every LiteBus completion handler, which is what makes a
    ///         record produced at <see cref="Observability" /> part of the transaction that commits here. Gate the
    ///         commit on <see cref="MessageCompletionContext.Outcome" />: the completion stage runs on every path, and a
    ///         failed mediation must roll back rather than commit.
    ///     </para>
    ///     <para>
    ///         The commit belongs in the completion stage rather than a post-handler because only the completion stage
    ///         sees how the mediation ended. A post-handler never runs when the main handler throws, so a commit placed
    ///         there cannot decide anything about a failure, and anything LiteBus writes afterwards is outside the
    ///         transaction by construction.
    ///     </para>
    ///     <para>
    ///         It sits above <see cref="ReservedCeiling" /> rather than on it so that the ceiling stays a boundary with
    ///         no handler on it. The gap between the two is the band for application infrastructure that has to run
    ///         after every LiteBus handler and still before the commit, such as a handler that flushes a buffered
    ///         projection. Registering there is ordered against the commit; registering on the ceiling itself used to
    ///         tie with it and resolve by registration sequence, which is assembly scan order.
    ///     </para>
    /// </remarks>
    public const int UnitOfWork = ReservedCeiling + 100;
}
