using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessageAsyncHandler2 : IAsyncMessageHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakePlainMessageAsyncHandler2));

        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler2)} executed!");

        return Task.CompletedTask;
    }
}