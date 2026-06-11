using LiteBus.Commands.Abstractions;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal sealed record ShipOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public string? IdempotencyKey { get; init; }
}