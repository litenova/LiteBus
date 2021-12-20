using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessageSyncHandlerWithResult : ISyncMessageHandler<FakePlainMessage, string>
{
    public string Handle(FakePlainMessage message)
    {
        Debug.WriteLine($"{nameof(FakePlainMessageSyncHandlerWithResult)} executed!");

        return "Hello World";
    }
}