using LiteBus.Events.Abstractions;

namespace LiteBus.Sample.Events;

/// <summary>
///     Records publication of the outbox event in the sample application log.
/// </summary>
public sealed class PaymentProcessedHandler : IEventHandler<PaymentProcessed>
{
    /// <summary>
    ///     The compiled log message used for processed payment events.
    /// </summary>
    private static readonly Action<ILogger, Guid, decimal, Exception?> LogPaymentProcessed =
        LoggerMessage.Define<Guid, decimal>(
            LogLevel.Information,
            new EventId(1, nameof(PaymentProcessed)),
            "Published PaymentProcessed for payment {PaymentId} with amount {Amount}");

    /// <summary>
    ///     The application logger.
    /// </summary>
    private readonly ILogger<PaymentProcessedHandler> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PaymentProcessedHandler" /> class.
    /// </summary>
    /// <param name="logger">The application logger.</param>
    public PaymentProcessedHandler(ILogger<PaymentProcessedHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(PaymentProcessed message, CancellationToken cancellationToken = default)
    {
        LogPaymentProcessed(_logger, message.PaymentId, message.Amount, null);

        return Task.CompletedTask;
    }
}
