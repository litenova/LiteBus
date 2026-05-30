using LiteBus.Events.Abstractions;

namespace LiteBus.Samples.V6.Events;

/// <summary>
///     Handles payment processed events replayed from the outbox into the local event pipeline.
/// </summary>
public sealed class PaymentProcessedHandler : IEventHandler<PaymentProcessed>
{
    private readonly ILogger<PaymentProcessedHandler> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PaymentProcessedHandler" /> class.
    /// </summary>
    /// <param name="logger">The logger used to record handled events.</param>
    public PaymentProcessedHandler(ILogger<PaymentProcessedHandler> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(PaymentProcessed @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Payment processed: {PaymentId} amount {Amount}",
            @event.PaymentId,
            @event.Amount);

        return Task.CompletedTask;
    }
}
