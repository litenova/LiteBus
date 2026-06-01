using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

internal sealed record ShipOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }
}
