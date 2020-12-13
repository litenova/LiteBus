using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class ColorQuery : IQuery<IEnumerable<string>>
    {
        public int Id { get; set; }
    }
    
    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<string>>
    {
        public Task<IEnumerable<string>> HandleAsync(ColorQuery input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MemoryDatabase.GetColors());
        }
    }
    
    public class ColorStreamQuery : IStreamQuery<string>
    {
        public int Id { get; set; }
    }
    
    public class ColorStreamQueryHandler : IStreamQueryHandler<ColorStreamQuery, string>
    {
        public IAsyncEnumerable<string> HandleAsync(ColorStreamQuery input, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}