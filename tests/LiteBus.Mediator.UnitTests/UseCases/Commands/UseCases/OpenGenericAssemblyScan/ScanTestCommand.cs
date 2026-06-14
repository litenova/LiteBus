using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.OpenGenericAssemblyScan;

public sealed class ScanTestCommand : IOpenGenericScanTestCommand, ICommand
{
    public List<Type> ExecutedTypes { get; } = new();
}