using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Sample.CommandBatches;

/// <summary>
///     Buffers sample domain writes and outbox envelopes for one mediation so a command batch commits them once.
/// </summary>
/// <remarks>
///     Handlers stage their work through <see cref="StageWrite" /> instead of applying it directly, and the sample
///     transactional outbox store stages envelopes through <see cref="StageOutboxEnvelope" />. <see cref="CommitAsync" />
///     appends the staged envelopes to the shared in-memory outbox store, applies the staged domain writes, and prints
///     what committed. <see cref="RollbackAsync" /> discards everything staged.
/// </remarks>
public sealed class SampleUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
{
    /// <summary>
    ///     The compiled log message used to print what one commit applied.
    /// </summary>
    private static readonly Action<ILogger, int, string, Exception?> LogCommitted =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(1, nameof(LogCommitted)),
            "Committed {WriteCount} staged writes: {Descriptions}");

    /// <summary>
    ///     The compiled log message used to print what one rollback discarded.
    /// </summary>
    private static readonly Action<ILogger, int, Exception?> LogRolledBack =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(2, nameof(LogRolledBack)),
            "Rolled back {WriteCount} staged writes without committing");

    /// <summary>
    ///     The shared in-memory outbox store that receives staged envelopes on commit.
    /// </summary>
    private readonly IOutboxStore _outboxStore;

    /// <summary>
    ///     The application logger.
    /// </summary>
    private readonly ILogger<SampleUnitOfWork> _logger;

    /// <summary>
    ///     The outbox envelopes staged since the last commit or rollback.
    /// </summary>
    private readonly List<OutboxEnvelope> _stagedEnvelopes = [];

    /// <summary>
    ///     The domain writes staged since the last commit or rollback, each with a description used for logging.
    /// </summary>
    private readonly List<(string Description, Action Apply)> _stagedWrites = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="SampleUnitOfWork" /> class.
    /// </summary>
    /// <param name="outboxStore">The shared in-memory outbox store that receives staged envelopes on commit.</param>
    /// <param name="logger">The application logger.</param>
    public SampleUnitOfWork(IOutboxStore outboxStore, ILogger<SampleUnitOfWork> logger)
    {
        ArgumentNullException.ThrowIfNull(outboxStore);
        ArgumentNullException.ThrowIfNull(logger);

        _outboxStore = outboxStore;
        _logger = logger;
    }

    /// <summary>
    ///     Stages one outbox envelope for the commit.
    /// </summary>
    /// <param name="envelope">The envelope to append when the mediation commits.</param>
    public void StageOutboxEnvelope(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _stagedEnvelopes.Add(envelope);
    }

    /// <summary>
    ///     Stages one domain write for the commit.
    /// </summary>
    /// <param name="description">The description printed when the write commits.</param>
    /// <param name="apply">The callback that applies the write.</param>
    public void StageWrite(string description, Action apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(apply);

        _stagedWrites.Add((description, apply));
    }

    /// <inheritdoc />
    public Task BeginAsync(CancellationToken cancellationToken = default)
    {
        _stagedEnvelopes.Clear();
        _stagedWrites.Clear();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_stagedEnvelopes.Count == 0 && _stagedWrites.Count == 0)
        {
            return;
        }

        var envelopes = _stagedEnvelopes.ToArray();
        _stagedEnvelopes.Clear();

        var writes = _stagedWrites.ToArray();
        _stagedWrites.Clear();

        if (envelopes.Length > 0)
        {
            await _outboxStore.AddBatchAsync(envelopes, cancellationToken).ConfigureAwait(false);
        }

        foreach (var write in writes)
        {
            write.Apply();
        }

        List<string> descriptions = [.. writes.Select(static write => write.Description)];

        foreach (var envelope in envelopes)
        {
            descriptions.Add($"outbox envelope {envelope.Id} ({envelope.ContractName})");
        }

        LogCommitted(_logger, descriptions.Count, string.Join("; ", descriptions), null);
    }

    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        var stagedCount = _stagedEnvelopes.Count + _stagedWrites.Count;

        _stagedEnvelopes.Clear();
        _stagedWrites.Clear();

        if (stagedCount > 0)
        {
            LogRolledBack(_logger, stagedCount, null);
        }

        return Task.CompletedTask;
    }
}
