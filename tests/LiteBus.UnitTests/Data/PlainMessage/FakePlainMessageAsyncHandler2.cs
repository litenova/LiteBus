using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessageAsyncHandler2 : IAsyncMessageHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakePlainMessageAsyncHandler2));

        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler2)} executed!");

        return Task.CompletedTask;
    }
}