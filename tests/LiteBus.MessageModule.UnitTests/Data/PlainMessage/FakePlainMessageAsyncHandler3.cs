using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessageAsyncHandler3 : IAsyncMessageHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakePlainMessageAsyncHandler3));

        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler3)} executed!");

        return Task.CompletedTask;
    }
}