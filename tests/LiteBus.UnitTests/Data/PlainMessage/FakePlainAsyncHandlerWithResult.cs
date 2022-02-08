using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainAsyncHandlerWithResult : IAsyncHandler<FakePlainMessage, string>
{
    public Task<string> HandleAsync(FakePlainMessage message,
                                    CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(FakePlainAsyncHandlerWithResult)} executed!");

        return Task.FromResult("Hello World");
    }
}