namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessagePostHandleAsyncHook
{
    public Task ExecuteAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}