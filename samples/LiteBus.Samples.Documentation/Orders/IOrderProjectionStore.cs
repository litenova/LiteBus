namespace LiteBus.Samples.Documentation.Orders;

/// <summary>
///     Writes order projections consumed by the documentation event handler.
/// </summary>
public interface IOrderProjectionStore
{
    /// <summary>
    ///     Inserts or updates the projection for one order.
    /// </summary>
    /// <param name="orderId">The order identifier.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the handler.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    Task UpsertAsync(Guid orderId, CancellationToken cancellationToken = default);
}
