using LiteBus.Commands.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
{
    private readonly CommandRecorder _recorder;

    public ShipOrderCommandHandler(CommandRecorder recorder)
    {
        _recorder = recorder;
    }

    public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record(message);
        return Task.CompletedTask;
    }
}