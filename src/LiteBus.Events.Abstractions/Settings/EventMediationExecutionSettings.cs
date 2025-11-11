using LiteBus.Messaging.Abstractions;

namespace LiteBus.Events.Abstractions;

/// <summary>
///     Configures event mediation execution behavior controlling how handlers are executed.
/// </summary>
/// <remarks>
///     <para>
///         Execution settings control the concurrency and ordering behavior of event handlers organized into priority
///         groups.
///         Handlers are grouped by their priority value (defined by <see cref="HandlerPriorityAttribute" />), where all
///         handlers with the same priority number form a priority group.
///     </para>
///     <para>
///         The execution behavior operates on two levels:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <strong>Priority Group Level:</strong> Controls how different priority groups execute relative
///                 to each other (sequential vs parallel)
///             </description>
///         </item>
///         <item>
///             <description>
///                 <strong>Handler Level:</strong> Controls how handlers within the same priority group execute
///                 (sequential vs parallel)
///             </description>
///         </item>
///     </list>
///     <para>
///         For example, with handlers having priorities [1, 1, 2, 2, 3]:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>Priority Group 1: Contains handlers with priority 1</description>
///         </item>
///         <item>
///             <description>Priority Group 2: Contains handlers with priority 2</description>
///         </item>
///         <item>
///             <description>Priority Group 3: Contains handlers with priority 3</description>
///         </item>
///     </list>
/// </remarks>
public sealed class EventMediationExecutionSettings
{
    /// <summary>
    ///     Gets or sets how different priority groups execute relative to each other.
    /// </summary>
    /// <value>
    ///     A <see cref="ConcurrencyMode" /> value that determines whether priority groups
    ///     execute sequentially or in parallel. The default is <see cref="ConcurrencyMode.Sequential" />.
    /// </value>
    /// <remarks>
    ///     <para>
    ///         Sequential mode ensures that all handlers in priority group 1 complete before any handler
    ///         in priority group 2 begins, maintaining strict priority ordering across groups.
    ///     </para>
    ///     <para>
    ///         Parallel mode ignores priority ordering and allows all priority groups to execute simultaneously,
    ///         effectively making all handlers run concurrently regardless of their priority values.
    ///     </para>
    /// </remarks>
    public ConcurrencyMode PriorityGroupsConcurrencyMode { get; init; } = ConcurrencyMode.Sequential;

    /// <summary>
    ///     Gets or sets how handlers within the same priority group execute.
    /// </summary>
    /// <value>
    ///     A <see cref="ConcurrencyMode" /> value that determines whether handlers
    ///     within the same priority group execute sequentially or in parallel.
    ///     The default is <see cref="ConcurrencyMode.Sequential" />.
    /// </value>
    /// <remarks>
    ///     <para>
    ///         This setting only affects handlers that have the same priority value and are therefore
    ///         in the same priority group.
    ///     </para>
    ///     <para>
    ///         Sequential mode executes handlers within the group one after another in registration order.
    ///         Parallel mode executes all handlers within the group simultaneously.
    ///     </para>
    /// </remarks>
    public ConcurrencyMode HandlersWithinSamePriorityConcurrencyMode { get; init; } = ConcurrencyMode.Sequential;
}