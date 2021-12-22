using System;

namespace LiteBus.UnitTests.Data.FakeCommand.Messages;

public class FakeCommandResult
{
    public FakeCommandResult(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; }
}