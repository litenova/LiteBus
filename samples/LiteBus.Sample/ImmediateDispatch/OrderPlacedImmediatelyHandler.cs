using LiteBus.Events.Abstractions;

namespace LiteBus.Sample.ImmediateDispatch;

/// <summary>
///     Records immediate dispatch of the placed-order event in the sample application log.
/// </summary>
public sealed class OrderPlacedImmediatelyHandler : IEventHandler<OrderPlacedImmediately>
{
    /// <summary>
    ///     The compiled log message used for immediately dispatched order events.
    /// </summary>
    private static readonly Action<ILogger, Guid, decimal, Exception?> LogOrderDispatchedImmediately =
        LoggerMessage.Define<Guid, decimal>(
            LogLevel.Information,
            new EventId(1, nameof(OrderPlacedImmediately)),
            "Dispatched OrderPlacedImmediately for order {OrderId} with amount {Amount} immediately after commit");

    /// <summary>
    ///     The application logger.
    /// </summary>
    private readonly ILogger<OrderPlacedImmediatelyHandler> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderPlacedImmediatelyHandler" /> class.
    /// </summary>
    /// <param name="logger">The application logger.</param>
    public OrderPlacedImmediatelyHandler(ILogger<OrderPlacedImmediatelyHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderPlacedImmediately message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogOrderDispatchedImmediately(_logger, message.OrderId, message.Amount, null);

        return Task.CompletedTask;
    }
}
