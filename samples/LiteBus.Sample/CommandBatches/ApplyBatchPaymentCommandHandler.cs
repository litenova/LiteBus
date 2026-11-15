using LiteBus.Commands.Abstractions;

namespace LiteBus.Sample.CommandBatches;

/// <summary>
///     Stages one batch payment in the sample unit of work so the batch commits it atomically.
/// </summary>
public sealed class ApplyBatchPaymentCommandHandler : ICommandHandler<ApplyBatchPaymentCommand>
{
    /// <summary>
    ///     The compiled log message used for staged batch payments.
    /// </summary>
    private static readonly Action<ILogger, Guid, decimal, Exception?> LogPaymentStaged =
        LoggerMessage.Define<Guid, decimal>(
            LogLevel.Information,
            new EventId(1, nameof(ApplyBatchPaymentCommand)),
            "Staged batch payment {PaymentId} with amount {Amount} for the batch commit");

    /// <summary>
    ///     The sample batch payment state store.
    /// </summary>
    private readonly BatchPaymentLedger _ledger;

    /// <summary>
    ///     The scoped unit of work that commits the staged payment.
    /// </summary>
    private readonly SampleUnitOfWork _unitOfWork;

    /// <summary>
    ///     The application logger.
    /// </summary>
    private readonly ILogger<ApplyBatchPaymentCommandHandler> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ApplyBatchPaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="ledger">The sample batch payment state store.</param>
    /// <param name="unitOfWork">The scoped unit of work that commits the staged payment.</param>
    /// <param name="logger">The application logger.</param>
    public ApplyBatchPaymentCommandHandler(
        BatchPaymentLedger ledger,
        SampleUnitOfWork unitOfWork,
        ILogger<ApplyBatchPaymentCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(logger);

        _ledger = ledger;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(
        ApplyBatchPaymentCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(message.Amount);

        _unitOfWork.StageWrite(
            $"batch payment {message.PaymentId} ({message.Amount})",
            () => _ledger.MarkProcessed(message.PaymentId, message.Amount));

        LogPaymentStaged(_logger, message.PaymentId, message.Amount, null);

        return Task.CompletedTask;
    }
}
