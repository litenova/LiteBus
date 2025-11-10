using LiteBus.Queries.Abstractions;

namespace LiteBus.Samples.Queries;

public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;