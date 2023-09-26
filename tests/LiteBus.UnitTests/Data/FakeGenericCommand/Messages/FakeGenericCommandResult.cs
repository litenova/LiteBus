using System;

namespace LiteBus.UnitTests.Data.FakeGenericCommand.Messages;

public sealed class FakeGenericCommandResult
{
    public FakeGenericCommandResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}