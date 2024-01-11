namespace LiteBus.UnitTests.Data.FakeCommand.Messages;

public sealed class FakeCommandResult
{
    public FakeCommandResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}