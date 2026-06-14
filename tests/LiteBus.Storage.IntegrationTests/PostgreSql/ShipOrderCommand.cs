using LiteBus.Commands.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed record ShipOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public string? IdempotencyKey { get; init; }
}