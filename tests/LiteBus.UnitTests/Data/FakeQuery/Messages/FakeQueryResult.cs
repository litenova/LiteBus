using System;

namespace LiteBus.UnitTests.Data.FakeQuery.Messages;

public sealed class FakeQueryResult
{
    public FakeQueryResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}