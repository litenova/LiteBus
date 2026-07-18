using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.PlainMessage;

public class FakePlainMessagePreHandler : IAsyncMessagePreHandler<FakePlainMessage>
{
    public Task PreHandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}