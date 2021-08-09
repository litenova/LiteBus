using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Queries
{
    public class ColorQuery : IQuery<IEnumerable<decimal>>
    {
        public int Id { get; set; }
    }

    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<decimal>>
    {
        public Task<IEnumerable<decimal>> HandleAsync(ColorQuery message,
                                                      CancellationToken cancellationToken = default)
        {
            var result = Task.FromResult(MemoryDatabase.GetNumbers());

            return result;
        }
    }
}