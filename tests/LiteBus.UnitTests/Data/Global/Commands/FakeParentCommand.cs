using System;
using System.Collections.Generic;

namespace LiteBus.UnitTests.Data.Global.Commands;

public abstract class FakeParentCommand
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}