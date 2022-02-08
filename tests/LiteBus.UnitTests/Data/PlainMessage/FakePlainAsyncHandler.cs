using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainAsyncHandler : IAsyncHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(FakePlainAsyncHandler)} executed!");

        return Task.CompletedTask;
    }
}