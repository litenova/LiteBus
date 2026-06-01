using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

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
