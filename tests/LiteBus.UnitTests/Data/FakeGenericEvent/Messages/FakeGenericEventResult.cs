namespace LiteBus.UnitTests.Data.FakeGenericEvent.Messages;

public class FakeGenericEventResult
{
    public FakeGenericEventResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}