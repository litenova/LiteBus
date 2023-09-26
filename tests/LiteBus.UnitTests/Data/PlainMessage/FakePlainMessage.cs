using System;
using System.Collections.Generic;

namespace LiteBus.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessage
{
    public List<Type> ExecutedTypes { get; } = new();
}