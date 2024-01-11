namespace LiteBus.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessage
{
    public List<Type> ExecutedTypes { get; } = new();
}