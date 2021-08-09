using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.PlainMessages
{
    public class PlainMessageAsyncHandlerWithResult : IAsyncMessageHandler<PlainMessage, string>
    {
        public Task<string> HandleAsync(PlainMessage message,
                                        CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(PlainMessageAsyncHandlerWithResult)} executed!");

            return Task.FromResult("Hello World");
        }
    }
}