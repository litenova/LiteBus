using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.WebApi.Queries;

namespace LiteBus.WebApi.Stream
{
    public class StreamNumbersQueryHandler : IStreamQueryHandler<StreamNumbersQuery, decimal>
    {
        public async IAsyncEnumerable<decimal> HandleAsync(StreamNumbersQuery message,
                                                           [EnumeratorCancellation]
                                                           CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(StreamNumbersQueryHandler)}: stream starting!");

            foreach (var number in MemoryDatabase.GetNumbers())
            {
                Debug.WriteLine($"{nameof(StreamNumbersQueryHandler)}: {number} streamed!");

                yield return await Task.FromResult(number);
            }

            Debug.WriteLine($"{nameof(StreamNumbersQueryHandler)}: stream finished!");
        }
    }
}