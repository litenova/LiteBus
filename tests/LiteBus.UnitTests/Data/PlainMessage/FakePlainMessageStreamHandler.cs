using System.Diagnostics;
using System.Runtime.CompilerServices;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.UnitTests.Data.PlainMessage;

public class FakePlainMessageStreamHandler : IStreamMessageHandler<FakePlainMessage, string>
{
    public async IAsyncEnumerable<string> StreamAsync(FakePlainMessage message,
                                                      [EnumeratorCancellation] CancellationToken cancellationToken =
                                                          default)
    {
        Debug.WriteLine($"{nameof(FakePlainMessageStreamHandler)} executed!");

        for (var i = 0; i < 10; i++)
        {
            yield return await Task.FromResult(i.ToString());
        }
    }
}