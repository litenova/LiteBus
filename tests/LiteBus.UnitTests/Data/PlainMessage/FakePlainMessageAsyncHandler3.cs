using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessageAsyncHandler3 : IAsyncMessageHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakePlainMessageAsyncHandler3));

        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler3)} executed!");

        return Task.CompletedTask;
    }
}