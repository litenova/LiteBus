using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     One command in a <see cref="RecurringCommandBatch" />, with the identity its pass sends it for.
/// </summary>
/// <param name="Command">The command the pass sends.</param>
/// <param name="Identity">
///     The identity the pass sends <paramref name="Command" /> for, or <see langword="null" /> to send it without an
///     identity.
/// </param>
/// <remarks>
///     How the pass sends the item depends on <see cref="RecurringCommandBatch.Isolation" />: an atomic batch sends
///     every item in one commit under one shared identity, while a per-item batch sends each item as its own
///     mediation with its own identity, so one failing item is logged and counted without stopping the rest. A
///     result-bearing command is sent without reading its result: the batch, not the item, reports
///     <see cref="IRecurringWorkReport" />.
/// </remarks>
public sealed record RecurringCommandItem(ICommand Command, ExecutionIdentity? Identity = null);
