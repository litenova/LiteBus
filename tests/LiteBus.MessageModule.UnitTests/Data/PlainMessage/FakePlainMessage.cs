namespace LiteBus.MessageModule.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessage
{
    public List<Type> ExecutedTypes { get; } = new();
}