using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage
{
    public class FakePlainMessagePreHandleAsyncHook : IPreHandleAsyncHook<FakePlainMessage>
    {
        public Task ExecuteAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}