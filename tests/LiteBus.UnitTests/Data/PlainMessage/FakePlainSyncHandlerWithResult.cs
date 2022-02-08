using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainSyncHandlerWithResult : ISyncHandler<FakePlainMessage, string>
{
    public string Handle(FakePlainMessage message)
    {
        Debug.WriteLine($"{nameof(FakePlainSyncHandlerWithResult)} executed!");

        return "Hello World";
    }
}