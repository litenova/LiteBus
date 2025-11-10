using LiteBus.Commands.Abstractions;

namespace LiteBus.Samples.Commands;

public sealed class ConfirmOrderCommandHandler : ICommandHandler<ConfirmOrderCommand>
{
    public Task HandleAsync(ConfirmOrderCommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Order with orderId {command.OrderId} confirmed.");
        return Task.CompletedTask;
    }
}