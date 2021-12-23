using System;

namespace LiteBus.UnitTests.Data.FakeQuery.Messages;

public class FakeQueryResult
{
    public FakeQueryResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}