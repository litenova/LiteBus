namespace LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

public sealed class FakeGenericQueryResult
{
    public FakeGenericQueryResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}