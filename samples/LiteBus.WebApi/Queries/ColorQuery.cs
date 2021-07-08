using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Queries
{
    public class ColorQuery : IQuery<IEnumerable<string>>
    {
        public int Id { get; set; }
    }

    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<string>>
    {
        public Task<IEnumerable<string>> HandleAsync(ColorQuery message,
                                                     CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MemoryDatabase.GetColors());
        }
    }

}