using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

internal sealed record ShipOrderCommand : ICommand
{
    public required Guid OrderId { get; init; }

    public string? IdempotencyKey { get; init; }
}

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

internal sealed record FaultyCommand : ICommand;

internal sealed class FaultyCommandHandler : ICommandHandler<FaultyCommand>
{
    public Task HandleAsync(FaultyCommand message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated handler failure.");
    }
}

internal sealed class CommandRecorder
{
    private readonly List<ShipOrderCommand> _commands = [];

    public IReadOnlyList<ShipOrderCommand> Commands => _commands;

    public void Record(ShipOrderCommand command) => _commands.Add(command);
}
