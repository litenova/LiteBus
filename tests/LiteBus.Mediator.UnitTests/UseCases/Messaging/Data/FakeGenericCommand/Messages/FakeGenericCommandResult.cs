namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.FakeGenericCommand.Messages;

public sealed class FakeGenericCommandResult
{
    public FakeGenericCommandResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}