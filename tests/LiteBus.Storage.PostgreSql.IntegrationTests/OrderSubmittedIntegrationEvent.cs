namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal sealed record OrderSubmittedIntegrationEvent
{
    public required Guid OrderId { get; init; }
}