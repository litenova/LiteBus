using LiteBus.Commands.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed class FaultyCommandHandler : ICommandHandler<FaultyCommand>
{
    public Task HandleAsync(FaultyCommand message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated handler failure.");
    }
}