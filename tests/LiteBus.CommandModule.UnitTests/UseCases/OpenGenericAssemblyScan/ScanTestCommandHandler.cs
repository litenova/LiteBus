using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.OpenGenericAssemblyScan;

public sealed class ScanTestCommandHandler : ICommandHandler<ScanTestCommand>
{
    public Task HandleAsync(ScanTestCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}
