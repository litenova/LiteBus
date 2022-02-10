using System;

namespace LiteBus.UnitTests.Data.FakeStreamQuery.Messages;

public class FakeStreamQueryResult
{
    public FakeStreamQueryResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}