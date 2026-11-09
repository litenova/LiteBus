using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Records what the smoke handlers observed, in order.
/// </summary>
internal sealed class SmokeRecorder
{
    /// <summary>
    ///     Holds the recorded entries.
    /// </summary>
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    ///     Gets the recorded entries in the order they were written.
    /// </summary>
    public IReadOnlyList<string> Entries => [.. _entries];

    /// <summary>
    ///     Records one entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    public void Record(string entry)
    {
        _entries.Enqueue(entry);
    }
}

/// <summary>
///     An entity with a key.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
internal interface ISmokeEntity<out TKey>
{
    /// <summary>
    ///     Gets the entity key.
    /// </summary>
    TKey Id { get; }
}

/// <summary>
///     An entity keyed by a <see cref="Guid" />.
/// </summary>
/// <param name="Id">The entity key.</param>
internal sealed record SmokeCustomer(Guid Id) : ISmokeEntity<Guid>;

/// <summary>
///     A query whose result nests the message's type parameter.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Item">The item the handler returns in a list.</param>
internal sealed record SmokeListQuery<T>(T Item) : IQuery<List<T>>;

/// <summary>
///     Returns the query's item in a one-element list.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class SmokeListQueryHandler<T> : IQueryHandler<SmokeListQuery<T>, List<T>>
{
    /// <inheritdoc />
    public Task<List<T>> HandleAsync(SmokeListQuery<T> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<T> { message.Item });
    }
}

/// <summary>
///     A command with two type parameters.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type the command returns.</typeparam>
internal sealed class SmokeCreateEntityCommand<TEntity, TKey> : ICommand<TKey>
    where TEntity : ISmokeEntity<TKey>
{
    /// <summary>
    ///     Gets the entity to create.
    /// </summary>
    public required TEntity EntityData { get; init; }
}

/// <summary>
///     Returns the key of the entity in the command.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class SmokeCreateEntityCommandHandler<TEntity, TKey> : ICommandHandler<SmokeCreateEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, ISmokeEntity<TKey>
{
    /// <inheritdoc />
    public Task<TKey> HandleAsync(SmokeCreateEntityCommand<TEntity, TKey> message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(message.EntityData.Id);
    }
}

/// <summary>
///     Records the typed result of a <see cref="SmokeCreateEntityCommand{TEntity, TKey}" />.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TKey">The key type.</typeparam>
internal sealed class SmokeCreateEntityCommandPostHandler<TEntity, TKey> : ICommandPostHandler<SmokeCreateEntityCommand<TEntity, TKey>, TKey>
    where TEntity : class, ISmokeEntity<TKey>
    where TKey : notnull
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeCreateEntityCommandPostHandler{TEntity, TKey}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public SmokeCreateEntityCommandPostHandler(SmokeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(SmokeCreateEntityCommand<TEntity, TKey> message, TKey? messageResult, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"post:{typeof(TEntity).Name}:{messageResult}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records every command it is closed over, through an open generic pre-handler over a bare type parameter.
/// </summary>
/// <typeparam name="TCommand">The command the registry closed this pre-handler over.</typeparam>
internal sealed class SmokeCommandAuditPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeCommandAuditPreHandler{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public SmokeCommandAuditPreHandler(SmokeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"audit:{typeof(TCommand).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     One streamed page.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Number">The one-based page number.</param>
/// <param name="Item">The item on the page.</param>
internal sealed record SmokePage<T>(int Number, T Item);

/// <summary>
///     A stream query whose item type nests the message's type parameter.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Item">The item every page carries.</param>
/// <param name="Pages">The number of pages to stream.</param>
internal sealed record SmokePagedQuery<T>(T Item, int Pages) : IStreamQuery<SmokePage<T>>;

/// <summary>
///     Streams the requested number of pages.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class SmokePagedQueryHandler<T> : IStreamQueryHandler<SmokePagedQuery<T>, SmokePage<T>>
{
    /// <inheritdoc />
    public async IAsyncEnumerable<SmokePage<T>> StreamAsync(
        SmokePagedQuery<T> message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var number = 1; number <= message.Pages; number++)
        {
            await Task.Yield();
            yield return new SmokePage<T>(number, message.Item);
        }
    }
}

/// <summary>
///     A generic event interface that non-generic events implement.
/// </summary>
/// <typeparam name="TEntity">The entity that changed.</typeparam>
internal interface ISmokeEntityChanged<TEntity> : IEvent;

/// <summary>
///     A non-generic event that implements <see cref="ISmokeEntityChanged{TEntity}" />.
/// </summary>
/// <param name="Entity">The customer that changed.</param>
internal sealed record SmokeCustomerChangedEvent(SmokeCustomer Entity) : ISmokeEntityChanged<SmokeCustomer>;

/// <summary>
///     Records the entity type of every <see cref="ISmokeEntityChanged{TEntity}" /> event.
/// </summary>
/// <typeparam name="TEntity">The entity that changed.</typeparam>
internal sealed class SmokeEntityChangedEventHandler<TEntity> : IEventHandler<ISmokeEntityChanged<TEntity>>
{
    /// <summary>
    ///     The recorder shared with the scenario.
    /// </summary>
    private readonly SmokeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeEntityChangedEventHandler{TEntity}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the scenario.</param>
    public SmokeEntityChangedEventHandler(SmokeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(ISmokeEntityChanged<TEntity> message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"changed:{typeof(TEntity).Name}");
        return Task.CompletedTask;
    }
}
