using LiteBus.Outbox.Abstractions;
using LiteBus.Samples.Documentation.Orders;

namespace LiteBus.Samples.Documentation;

/// <summary>
///     Provides compiled source for the transactional outbox enqueue snippet used by the registration guide.
/// </summary>
internal static class OutboxEnqueueSnippet
{
    /// <summary>
    ///     Stages an order event through the transactional outbox before the commit.
    /// </summary>
    /// <param name="transactionalOutbox">The context-bound outbox writer.</param>
    /// <param name="order">The order being placed.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the handler.</param>
    internal static async Task StageOrderSubmittedAsync(
        ITransactionalOutbox<OrdersDbContext> transactionalOutbox,
        Order order,
        CancellationToken cancellationToken)
    {
        // <docs-snippet efcore-outbox-enqueue>
        await transactionalOutbox.EnqueueAsync(
            new OrderSubmitted { OrderId = order.Id },
            cancellationToken).ConfigureAwait(false);
        // </docs-snippet>
    }
}
