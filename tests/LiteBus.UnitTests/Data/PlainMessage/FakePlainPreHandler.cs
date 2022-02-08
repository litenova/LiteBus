using System;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainPreHandler : IAsyncPreHandler<FakePlainMessage>
{
    public Task HandleAsync(IHandleContext<FakePlainMessage> context)
    {
        throw new NotImplementedException();
    }
}