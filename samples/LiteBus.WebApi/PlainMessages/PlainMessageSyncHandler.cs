using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.PlainMessages;

public class PlainMessageSyncHandler : ISyncMessageHandler<PlainMessage>
{
    public void Handle(PlainMessage message)
    {
        Debug.WriteLine($"{nameof(PlainMessageSyncHandler)} executed!");
    }
}