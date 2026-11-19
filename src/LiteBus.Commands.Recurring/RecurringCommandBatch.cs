using System;
using System.Collections.Generic;
using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     The commands one pass of a recurring batch loop sends.
/// </summary>
/// <remarks>
///     <para>
///         Produced by <see cref="IRecurringCommandBatchSource.CreateBatchAsync" />. An empty <see cref="Items" /> list
///         sends nothing and the loop waits <see cref="RecurringCommandHostOptions.Interval" />.
///     </para>
///     <para>
///         By default (<see cref="RecurringBatchIsolation.Atomic" />) the pass sends the items as one batch through
///         <see cref="ICommandBatchMediator.TrySendManyAsync" />: the commands run one at a time, in list order,
///         inside one dispatch scope with one unit-of-work commit, and the first denied, invalid, faulted, or
///         cancelled command rolls the whole batch back. Every item must then carry the same identity, or none; a
///         batch that mixes identities fails the pass, because one atomic batch runs under one identity.
///     </para>
///     <para>
///         With <see cref="RecurringBatchIsolation.PerItem" /> the pass sends each item as its own mediation with its
///         own identity. One failing item is logged and counted and never stops the rest of the batch; when any item
///         failed, the pass waits <see cref="RecurringCommandHostOptions.ErrorBackoff" /> instead of the interval.
///     </para>
///     <para>
///         A command that needs the value another command produces does not belong here: batch commands cannot see
///         each other's results, and a result-bearing command is sent without reading its result.
///     </para>
/// </remarks>
public sealed record RecurringCommandBatch : IRecurringWorkReport
{
    /// <summary>
    ///     Gets an empty batch, which sends nothing and waits the interval.
    /// </summary>
    public static RecurringCommandBatch Empty { get; } = new() { Items = [] };

    /// <summary>
    ///     Gets the items the pass sends, in list order.
    /// </summary>
    public required IReadOnlyList<RecurringCommandItem> Items { get; init; }

    /// <summary>
    ///     Gets how the pass sends <see cref="Items" />: as one atomic batch, or one mediation per item.
    /// </summary>
    /// <value>
    ///     <see cref="RecurringBatchIsolation.Atomic" /> unless the batch was built with
    ///     <see cref="RecurringBatchIsolation.PerItem" />.
    /// </value>
    public RecurringBatchIsolation Isolation { get; init; } = RecurringBatchIsolation.Atomic;

    /// <inheritdoc />
    public bool HasMoreWork { get; init; }

    /// <summary>
    ///     Creates an atomic batch from the given commands, each sent without an identity.
    /// </summary>
    /// <param name="commands">The commands the pass sends, in list order.</param>
    /// <param name="hasMoreWork">
    ///     <see langword="true" /> when the source filled its read and more is waiting, so the loop starts the next
    ///     pass without waiting.
    /// </param>
    /// <returns>The batch the pass sends as one atomic batch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commands" /> is <see langword="null" />.</exception>
    public static RecurringCommandBatch From(IReadOnlyList<ICommand> commands, bool hasMoreWork = false)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var items = new RecurringCommandItem[commands.Count];

        for (var index = 0; index < commands.Count; index++)
        {
            items[index] = new RecurringCommandItem(commands[index]);
        }

        return new RecurringCommandBatch { Items = items, HasMoreWork = hasMoreWork };
    }

    /// <summary>
    ///     Creates a batch from the given items.
    /// </summary>
    /// <param name="items">The items the pass sends, in list order.</param>
    /// <param name="hasMoreWork">
    ///     <see langword="true" /> when the source filled its read and more is waiting, so the loop starts the next
    ///     pass without waiting.
    /// </param>
    /// <param name="isolation">
    ///     How the pass sends <paramref name="items" />: as one atomic batch, or one mediation per item.
    /// </param>
    /// <returns>The batch the pass sends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items" /> is <see langword="null" />.</exception>
    public static RecurringCommandBatch From(
        IReadOnlyList<RecurringCommandItem> items,
        bool hasMoreWork = false,
        RecurringBatchIsolation isolation = RecurringBatchIsolation.Atomic)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new RecurringCommandBatch { Items = items, HasMoreWork = hasMoreWork, Isolation = isolation };
    }
}
