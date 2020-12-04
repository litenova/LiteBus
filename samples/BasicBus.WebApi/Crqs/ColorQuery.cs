using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BasicBus.Abstractions;

namespace BasicBus.WebApi.Crqs
{
    public class ColorQuery : IQuery<IEnumerable<string>>
    {
        
    }
    
    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<string>>
    {
        public Task<IEnumerable<string>> HandleAsync(ColorQuery input, CancellationToken cancellation = default)
        {
            return Task.FromResult(MemoryDatabase.GetColors());
        }
    }
}