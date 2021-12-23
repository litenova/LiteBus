using System;

namespace LiteBus.UnitTests.Data.FakeGenericQuery.Messages;

public class FakeGenericQueryResult
{
    public FakeGenericQueryResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}