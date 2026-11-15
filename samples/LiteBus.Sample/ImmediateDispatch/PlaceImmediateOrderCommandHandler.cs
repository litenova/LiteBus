using LiteBus.Commands.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Sample.CommandBatches;

namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Stages one immediate order and its event so both commit in the same unit of work.
/// </summary>
public sealed class PlaceImmediateOrderCommandHandler : ICommandHandler<PlaceImmediateOrderCommand>
{
    /// <summary>
    ///     The sample immediate order state store.
    /// </summary>
    private readonly ImmediateOrderLedger _ledger;

    /// <summary>
    ///     The scoped unit of work that commits the staged order and envelope.
    /// </summary>
    private readonly SampleUnitOfWork _unitOfWork;

    /// <summary>
    ///     The transactional event writer.
    /// </summary>
    private readonly ITransactionalOutbox _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlaceImmediateOrderCommandHandler" /> class.
    /// </summary>
    /// <param name="ledger">The sample immediate order state store.</param>
    /// <param name="unitOfWork">The scoped unit of work that commits the staged order and envelope.</param>
    /// <param name="outbox">The transactional event writer.</param>
    public PlaceImmediateOrderCommandHandler(
        ImmediateOrderLedger ledger,
        SampleUnitOfWork unitOfWork,
        ITransactionalOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(outbox);

        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        PlaceImmediateOrderCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(message.Amount);

        _unitOfWork.StageWrite(
            $"immediate order {message.OrderId} ({message.Amount})",
            () => _ledger.MarkPlaced(message.OrderId, message.Amount));

        await _outbox.EnqueueAsync(
                new OrderPlacedImmediately(message.OrderId, message.Amount),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
