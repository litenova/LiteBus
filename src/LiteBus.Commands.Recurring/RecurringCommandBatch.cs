using System;
using System.Collections.Generic;
using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     The commands one pass of a recurring batch loop sends as a single batch.
/// </summary>
/// <remarks>
///     <para>
///         Produced by <see cref="IRecurringCommandBatchSource.CreateBatchAsync" /> and sent through
///         <see cref="ICommandBatchMediator.TrySendManyAsync" />. An empty <see cref="Commands" /> list sends nothing
///         and the loop waits <see cref="RecurringCommandHostOptions.Interval" />.
///     </para>
///     <para>
///         The commands run one at a time, in list order, inside one dispatch scope with one unit-of-work commit. A
///         command that needs the value another command produces does not belong here: batch commands cannot see each
///         other's results.
///     </para>
/// </remarks>
public sealed record RecurringCommandBatch : IRecurringWorkReport
{
    /// <summary>
    ///     Gets an empty batch, which sends nothing and waits the interval.
    /// </summary>
    public static RecurringCommandBatch Empty { get; } = new() { Commands = [] };

    /// <summary>
    ///     Gets the commands the pass sends as one batch, in list order.
    /// </summary>
    public required IReadOnlyList<ICommand> Commands { get; init; }

    /// <inheritdoc />
    public bool HasMoreWork { get; init; }

    /// <summary>
    ///     Creates a batch from the given commands.
    /// </summary>
    /// <param name="commands">The commands the pass sends as one batch, in list order.</param>
    /// <param name="hasMoreWork">
    ///     <see langword="true" /> when the source filled its read and more is waiting, so the loop starts the next
    ///     pass without waiting.
    /// </param>
    /// <returns>The batch the pass sends.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="commands" /> is <see langword="null" />.</exception>
    public static RecurringCommandBatch From(IReadOnlyList<ICommand> commands, bool hasMoreWork = false)
    {
        ArgumentNullException.ThrowIfNull(commands);

        return new RecurringCommandBatch { Commands = commands, HasMoreWork = hasMoreWork };
    }
}
