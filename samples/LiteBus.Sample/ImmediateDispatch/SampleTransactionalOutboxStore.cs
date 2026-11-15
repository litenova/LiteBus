using LiteBus.Outbox.Abstractions;
using LiteBus.Sample.CommandBatches;

namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Stages outbox envelopes in the sample unit of work so they commit with the mediation's domain writes.
/// </summary>
public sealed class SampleTransactionalOutboxStore : ITransactionalOutboxStore
{
    /// <summary>
    ///     The scoped unit of work that holds the staged envelopes until the commit.
    /// </summary>
    private readonly SampleUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SampleTransactionalOutboxStore" /> class.
    /// </summary>
    /// <param name="unitOfWork">The scoped unit of work that holds the staged envelopes until the commit.</param>
    public SampleTransactionalOutboxStore(SampleUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);

        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<OutboxAppendResult> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        _unitOfWork.StageOutboxEnvelope(envelope);

        return Task.FromResult(new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxAppendResult>> AddBatchAsync(
        IReadOnlyList<OutboxEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);

        var results = new OutboxAppendResult[envelopes.Count];

        for (var index = 0; index < envelopes.Count; index++)
        {
            _unitOfWork.StageOutboxEnvelope(envelopes[index]);
            results[index] = new OutboxAppendResult(envelopes[index], OutboxEnqueueOutcome.Enqueued);
        }

        return Task.FromResult<IReadOnlyList<OutboxAppendResult>>(results);
    }
}
