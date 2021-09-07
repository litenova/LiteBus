using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage
{
    public class FakePlainMessagePreHandler : IMessagePreHandler<FakePlainMessage>
    {
        public Task PreHandleAsync(IHandleContext<FakePlainMessage> context)
        {
            throw new NotImplementedException();
        }
    }
}