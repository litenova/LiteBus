using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class PlainMessage
    {
        public int Number { get; set; }
    }

    public class PlainMessageHandler : IMessageHandler<PlainMessage, Task<int>>
    {
        public Task<int> HandleAsync(PlainMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(message.Number * -1);
        }
    }
}