using System.Diagnostics;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.PlainMessages;

public class PlainMessageSyncHandlerWithResult : ISyncMessageHandler<PlainMessage, string>
{
    public string Handle(PlainMessage message)
    {
        Debug.WriteLine($"{nameof(PlainMessageSyncHandlerWithResult)} executed!");

        return "Hello World";
    }
}