using LiteBus.Commands.Abstractions;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal sealed class FaultyCommandHandler : ICommandHandler<FaultyCommand>
{
    public Task HandleAsync(FaultyCommand message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated handler failure.");
    }
}
