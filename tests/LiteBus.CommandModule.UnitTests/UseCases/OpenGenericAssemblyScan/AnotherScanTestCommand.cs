using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.OpenGenericAssemblyScan;

public sealed class AnotherScanTestCommand : IOpenGenericScanTestCommand, ICommand
{
    public List<Type> ExecutedTypes { get; } = new();
}
