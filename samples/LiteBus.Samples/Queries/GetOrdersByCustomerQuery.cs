using LiteBus.Queries.Abstractions;

namespace LiteBus.Samples.Queries;

public sealed record GetOrdersByCustomerQuery(string CustomerId) : IQuery<IEnumerable<OrderDto>>;