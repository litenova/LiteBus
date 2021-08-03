using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Queries
{
    public class ColorQuery : IQuery<IEnumerable<string>>
    {
        public int Id { get; set; }
    }

    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<string>>
    {
        public ITask<IEnumerable<string>> HandleAsync(ColorQuery message,
                                                      CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MemoryDatabase.GetColors()).AsITask();
        }
    }

}