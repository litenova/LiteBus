using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.PlainMessages
{
    public class PlainMessageAsyncHandler : IAsyncMessageHandler<PlainMessage>
    {
        public ITask HandleAsync(PlainMessage message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(PlainMessageAsyncHandler)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
}