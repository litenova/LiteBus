using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class SimpleMessage
    {
        public int Number { get; set; }
    }
    
    public class SimpleMessageHandler : IMessageHandler<SimpleMessage, Task<int>>
    {
        public Task<int> HandleAsync(SimpleMessage message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(message.Number * -1);
        }
    }
}