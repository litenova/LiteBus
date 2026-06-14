namespace LiteBus.Mediator.UnitTests.UseCases.Messaging.Data.PlainMessage;

public class FakePlainMessagePostHandleAsyncHook
{
    public Task ExecuteAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}