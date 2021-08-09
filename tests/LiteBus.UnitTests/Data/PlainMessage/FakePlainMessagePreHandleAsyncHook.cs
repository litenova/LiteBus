using System.Threading;
using LiteBus.Messaging.Abstractions;
using MorseCode.ITask;

namespace LiteBus.UnitTests.Data.PlainMessage
{
    public class FakePlainMessagePreHandleAsyncHook : IPreHandleAsyncHook<FakePlainMessage>
    {
        public ITask ExecuteAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}