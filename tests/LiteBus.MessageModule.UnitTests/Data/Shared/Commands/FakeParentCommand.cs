namespace LiteBus.MessageModule.UnitTests.Data.Shared.Commands;

public abstract class FakeParentCommand
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}