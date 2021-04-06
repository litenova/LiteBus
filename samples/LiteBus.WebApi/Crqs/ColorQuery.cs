using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class ColorQuery : IQuery<IEnumerable<string>>
    {
        public int Id { get; set; }
    }

    public class ColorQueryHandler : IQueryHandler<ColorQuery, IEnumerable<string>>
    {
        public Task<IEnumerable<string>> Handle(ColorQuery message)
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
        public IAsyncEnumerable<string> Handle(ColorStreamQuery message)
        {
            throw new NotImplementedException();
        }
    }
}