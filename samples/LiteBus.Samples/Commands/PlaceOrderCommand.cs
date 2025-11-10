using LiteBus.Commands.Abstractions;
using LiteBus.Samples.Queries;

namespace LiteBus.Samples.Commands;

public sealed record PlaceOrderCommand(string CustomerId, List<PlaceOrderLineItemDto> LineItems) : ICommand<Guid>;