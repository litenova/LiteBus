using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Queries
{
    public class ColorStreamQuery : IStreamQuery<string>
    {
        public int Id { get; set; }
    }

    public class ColorStreamQueryHandler : IStreamQueryHandler<ColorStreamQuery, string>
    {
        public async IAsyncEnumerable<string> HandleAsync(ColorStreamQuery message,
                                                          [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Debug.WriteLine($"{nameof(ColorStreamQueryHandler)} executed!");

            foreach (var i in Enumerable.Range(1, 10))
            {
                yield return await Task.FromResult(i.ToString());
            }
        }
    }
}