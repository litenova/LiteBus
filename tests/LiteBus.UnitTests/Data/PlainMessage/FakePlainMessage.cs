using System;
using System.Collections.Generic;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessage
{
    public List<Type> ExecutedTypes { get; } = new();
}