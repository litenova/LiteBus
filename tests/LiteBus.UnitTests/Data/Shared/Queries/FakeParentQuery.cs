using System;
using System.Collections.Generic;

namespace LiteBus.UnitTests.Data.Shared.Queries;

public abstract class FakeParentQuery
{
    public List<Type> ExecutedTypes { get; } = new();

    public Guid CorrelationId { get; } = Guid.NewGuid();
}