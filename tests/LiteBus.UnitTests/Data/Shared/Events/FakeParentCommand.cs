using System;
using System.Collections.Generic;

namespace LiteBus.UnitTests.Data.Shared.Events;

public abstract class FakeParentEvent
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}