using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Queries
{
    public class StreamNumbersQuery : IStreamQuery<decimal>
    {
        
    }

    public class StreamNumbersQueryHandler : IStreamQueryHandler<StreamNumbersQuery, decimal>
    {
        public async IAsyncEnumerable<decimal> HandleAsync(StreamNumbersQuery message,
                                                          [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(StreamNumbersQueryHandler)} executed!");

            foreach (var number in MemoryDatabase.GetNumbers())
            {
                yield return await Task.FromResult(number);
            }
        }
    }
}