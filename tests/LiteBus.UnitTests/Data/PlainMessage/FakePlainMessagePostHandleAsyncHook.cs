using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessageHandleAsyncHook
{
    public Task ExecuteAsync(FakePlainMessage message, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}