using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessageAsyncHandler : IAsyncMessageHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler)} executed!");

        return Task.CompletedTask;
    }
}