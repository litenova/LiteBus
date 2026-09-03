using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     The aggregate a guard loads and a handler acts on.
/// </summary>
/// <param name="Id">The aggregate identifier.</param>
public sealed record Occurrence(string Id);

/// <summary>
///     The identifier value object used as a keyed store key, standing in for an application's own identifier.
/// </summary>
/// <param name="Value">The identifier value.</param>
public sealed record OccurrenceId(string Value);

/// <summary>
///     Counts how many times the aggregate was loaded, which is the whole point of the handoff.
/// </summary>
public sealed class AggregateLoadCounter
{
    /// <summary>
    ///     Gets the identifiers loaded, in order.
    /// </summary>
    public List<string> Ids { get; } = [];

    /// <summary>
    ///     Gets the number of loads recorded.
    /// </summary>
    public int Count => Ids.Count;

    /// <summary>
    ///     Loads the aggregate, recording the attempt.
    /// </summary>
    /// <param name="id">The aggregate identifier.</param>
    /// <returns>The aggregate, or <see langword="null" /> when the identifier is not known.</returns>
    public Occurrence? Load(string id)
    {
        Ids.Add(id);
        return id == "missing" ? null : new Occurrence(id);
    }
}

/// <summary>
///     A command whose authorization decision depends on state the guard has to load.
/// </summary>
internal sealed class ArchiveOccurrenceCommand : ICommand<string>
{
    /// <summary>
    ///     Gets or sets the identifier of the occurrence to archive.
    /// </summary>
    public string OccurrenceId { get; set; } = string.Empty;
}

/// <summary>
///     Loads the occurrence, denies when it does not exist, and hands the loaded instance to the handler.
/// </summary>
internal sealed class ArchiveOccurrenceGuard : ICommandGuard<ArchiveOccurrenceCommand>
{
    /// <summary>
    ///     The loader shared with the test.
    /// </summary>
    private readonly AggregateLoadCounter _loads;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ArchiveOccurrenceGuard" /> class.
    /// </summary>
    /// <param name="loads">The loader shared with the test.</param>
    public ArchiveOccurrenceGuard(AggregateLoadCounter loads)
    {
        _loads = loads;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ArchiveOccurrenceCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var occurrence = _loads.Load(message.OccurrenceId);

        if (occurrence is null)
        {
            return Task.FromResult(Verdict.Deny("the occurrence does not exist"));
        }

        AmbientExecutionContext.Current.Data.Set(occurrence);
        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Acts on the occurrence the guard already loaded, without loading it again.
/// </summary>
internal sealed class ArchiveOccurrenceCommandHandler : ICommandHandler<ArchiveOccurrenceCommand, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(ArchiveOccurrenceCommand message, CancellationToken cancellationToken = default)
    {
        var occurrence = AmbientExecutionContext.Current.Data.Get<Occurrence>();
        return Task.FromResult($"archived {occurrence.Id}");
    }
}
