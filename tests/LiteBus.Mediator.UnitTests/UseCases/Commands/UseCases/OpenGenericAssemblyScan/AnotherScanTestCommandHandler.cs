using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.OpenGenericAssemblyScan;

public sealed class AnotherScanTestCommandHandler : ICommandHandler<AnotherScanTestCommand>
{
    public Task HandleAsync(AnotherScanTestCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}