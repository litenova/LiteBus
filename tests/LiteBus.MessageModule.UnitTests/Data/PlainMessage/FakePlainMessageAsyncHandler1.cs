using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.MessageModule.UnitTests.Data.PlainMessage;

public sealed class FakePlainMessageAsyncHandler1 : IAsyncMessageHandler<FakePlainMessage>
{
    public Task HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(FakePlainMessageAsyncHandler1));

        Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler1)} executed!");

        return Task.CompletedTask;
    }
}