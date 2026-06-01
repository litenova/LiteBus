namespace LiteBus.Inbox.Ingress.Amqp.IntegrationTests;

internal sealed class CommandRecorder
{
    private readonly List<ShipOrderCommand> _commands = [];

    public IReadOnlyList<ShipOrderCommand> Commands => _commands;

    public void Record(ShipOrderCommand command) => _commands.Add(command);
}
