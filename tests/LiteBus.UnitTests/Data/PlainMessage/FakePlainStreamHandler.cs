using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainStreamHandler : IStreamHandler<FakePlainMessage, string>
{
    public async IAsyncEnumerable<string> HandleAsync(FakePlainMessage message,
                                                      [EnumeratorCancellation] CancellationToken cancellationToken =
                                                          default)
    {
        Debug.WriteLine($"{nameof(FakePlainStreamHandler)} executed!");

        for (var i = 0; i < 10; i++)
        {
            yield return await Task.FromResult(i.ToString());
        }
    }
}