using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.WebApi.PlainMessages;

public class PlainMessageStreamHandler : IStreamMessageHandler<PlainMessage, string>
{
    public async IAsyncEnumerable<string> HandleAsync(PlainMessage message,
                                                      [EnumeratorCancellation] CancellationToken cancellationToken =
                                                          default)
    {
        Debug.WriteLine($"{nameof(PlainMessageStreamHandler)} executed!");

        for (var i = 0; i < 10; i++)
        {
            yield return await Task.FromResult(i.ToString());
        }
    }
}