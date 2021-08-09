using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using MorseCode.ITask;

namespace LiteBus.UnitTests.Data.PlainMessage
{
    public class FakePlainMessageStreamHandler : IStreamMessageHandler<FakePlainMessage, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(FakePlainMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(FakePlainMessageStreamHandler)} executed!");

            for (var i = 0; i < 10; i++)
            {
                yield return await Task.FromResult(i.ToString());
            }
        }
    }
}