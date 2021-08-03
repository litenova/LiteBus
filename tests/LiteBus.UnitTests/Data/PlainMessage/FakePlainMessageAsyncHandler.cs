using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using MorseCode.ITask;

namespace LiteBus.UnitTests.Data.PlainMessage
{
    public class FakePlainMessageAsyncHandler : IAsyncMessageHandler<FakePlainMessage>
    {
        public ITask HandleAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(FakePlainMessageAsyncHandler)} executed!");

            return Task.CompletedTask.AsITask();
        }
    }
}