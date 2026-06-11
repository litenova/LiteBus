using LiteBus.Commands.Abstractions;

namespace LiteBus.Transport.InMemory.IntegrationTests;

/// <summary>
///     Test command payload accepted through transport ingress.
/// </summary>
internal sealed record ShipOrderCommand : ICommand
{
    /// <summary>
    ///     Gets the order identifier.
    /// </summary>
    public required Guid OrderId { get; init; }
}