using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.OpenGenericAssemblyScan;

public sealed class AnotherScanTestCommand : IOpenGenericScanTestCommand, ICommand
{
    public List<Type> ExecutedTypes { get; } = new();
}