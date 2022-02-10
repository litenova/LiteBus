using System;

namespace LiteBus.UnitTests.Data.FakeGenericStreamQuery.Messages;

public class FakeGenericStreamQueryResult
{
    public FakeGenericStreamQueryResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}