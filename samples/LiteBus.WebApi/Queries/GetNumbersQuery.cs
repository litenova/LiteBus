using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Queries;

public class GetNumbersQuery : IQuery<IEnumerable<decimal>>
{
}

public class GetNumbersQueryHandler : IQueryHandler<GetNumbersQuery, IEnumerable<decimal>>
{
    public Task<IEnumerable<decimal>> HandleAsync(GetNumbersQuery message,
                                                  CancellationToken cancellationToken = default)
    {
        Debug.WriteLine($"{nameof(GetNumbersQueryHandler)} executed!");

        return Task.FromResult(MemoryDatabase.GetNumbers());
    }
}