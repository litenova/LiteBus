using LiteBus.Commands.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Sample.Events;

namespace LiteBus.Sample.Commands;

/// <summary>
///     Updates the sample ledger and records a durable event for later in-process publication.
/// </summary>
public sealed class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand>
{
    /// <summary>
    ///     The sample payment state store.
    /// </summary>
    private readonly PaymentLedger _ledger;

    /// <summary>
    ///     The durable event writer.
    /// </summary>
    private readonly IOutbox _outbox;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ProcessPaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="ledger">The sample payment state store.</param>
    /// <param name="outbox">The durable event writer.</param>
    public ProcessPaymentCommandHandler(PaymentLedger ledger, IOutbox outbox)
    {
        _ledger = ledger;
        _outbox = outbox;
    }

    /// <inheritdoc />
    public async Task HandleAsync(
        ProcessPaymentCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(message.Amount);

        _ledger.MarkProcessed(message.PaymentId, message.Amount);
        await _outbox.EnqueueAsync(
                new PaymentProcessed(message.PaymentId, message.Amount),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
