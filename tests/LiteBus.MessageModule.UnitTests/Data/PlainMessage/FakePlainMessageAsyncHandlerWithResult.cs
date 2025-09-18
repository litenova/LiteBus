using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.PlainMessage;

public class FakePlainMessageAsyncHandlerWithResult : IAsyncMessageHandler<FakePlainMessage, string>
{
    public Task<string> HandleAsync(FakePlainMessage message,
                                    CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandlerWithResult)} executed!");

        return Task.FromResult("Hello World");
    }
}