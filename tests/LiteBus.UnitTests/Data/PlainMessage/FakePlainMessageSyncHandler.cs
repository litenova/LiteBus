using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage
{
    public class FakePlainMessageSyncHandler : ISyncMessageHandler<FakePlainMessage>
    {
        public void Handle(FakePlainMessage message)
        {
            Debug.WriteLine($"{nameof(FakePlainMessageSyncHandler)} executed!");
        }
    }
}