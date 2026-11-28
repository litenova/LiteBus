using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Reports the per-type domain event decisions the outbox stages with and the policy for undecided types.
/// </summary>
/// <remarks>
///     <para>
///         The section snapshots the collected <see cref="DomainEventDecisions" /> while the outbox module builds, so
///         the composition export names every decided event type with its kind and the policy an undecided type
///         follows. A staged event type is reported with the <see cref="DomainEventDecisionKind.Stage" /> kind,
///         distinct from the <see cref="UndecidedDomainEventPolicy.StageAsIs" /> policy that stages an undecided
///         type unchanged by default.
///     </para>
///     <para>
///         The value <see cref="GetData" /> returns holds ordinal string keys, lists, and primitives, so the
///         composition export stays deterministic.
///     </para>
/// </remarks>
public sealed class OutboxDomainEventCompositionSection : ICompositionSectionContributor
{
    /// <summary>
    ///     The registered per-type decisions, in ordinal event type name order. Snapshot at construction.
    /// </summary>
    private readonly IReadOnlyList<DomainEventDecisionRecord> _decisions;

    /// <summary>
    ///     What the staging handler does with a domain event type that has no registered decision. Snapshot at construction.
    /// </summary>
    private readonly UndecidedDomainEventPolicy _undecidedPolicy;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxDomainEventCompositionSection" /> class.
    /// </summary>
    /// <param name="decisions">The per-type domain event decisions collected from the outbox builder.</param>
    internal OutboxDomainEventCompositionSection(DomainEventDecisions decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        _decisions = decisions.DescribeDecisions();
        _undecidedPolicy = decisions.UndecidedPolicy;
    }

    /// <summary>
    ///     Gets the registered per-type decisions, in ordinal event type name order.
    /// </summary>
    public IReadOnlyList<DomainEventDecisionRecord> Decisions => _decisions;

    /// <summary>
    ///     Gets what the staging handler does with a domain event type that has no registered decision.
    /// </summary>
    public UndecidedDomainEventPolicy UndecidedPolicy => _undecidedPolicy;

    /// <inheritdoc />
    public string SectionName => "outbox-domain-events";

    /// <inheritdoc />
    public object? GetData()
    {
        List<SortedDictionary<string, object?>> decisions = new(_decisions.Count);

        foreach (var decision in _decisions)
        {
            decisions.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["eventType"] = decision.EventType.FullName ?? decision.EventType.Name,
                ["kind"] = decision.Kind.ToString(),
            });
        }

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["decisions"] = decisions,
            ["undecidedPolicy"] = _undecidedPolicy.ToString(),
        };
    }
}
