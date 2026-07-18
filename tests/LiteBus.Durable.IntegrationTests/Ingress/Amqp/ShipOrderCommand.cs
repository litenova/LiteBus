using LiteBus.Commands.Abstractions;

namespace LiteBus.Durable.IntegrationTests.Ingress.Amqp;

internal sealed record ShipOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }
}