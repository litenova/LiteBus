namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.PlainMessage;

public sealed class FakePlainMessage
{
    public List<Type> ExecutedTypes { get; } = new();
}