using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainSyncHandler : ISyncHandler<FakePlainMessage>
{
    public void Handle(FakePlainMessage message)
    {
        Debug.WriteLine($"{nameof(FakePlainSyncHandler)} executed!");
    }
}