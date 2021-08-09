using System.Collections.Generic;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;
using LiteBus.WebApi.Queries;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.WebApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class QueryController : ControllerBase
    {
        private readonly IQueryMediator _queryMediator;

        public QueryController(IQueryMediator queryMediator)
        {
            _queryMediator = queryMediator;
        }

        [HttpGet]
        public Task<IEnumerable<decimal>> GetNumbers()
        {
            return _queryMediator.QueryAsync(new GetNumbersQuery());
        }

        [HttpGet("stream")]
        public IAsyncEnumerable<decimal> StreamNumbers()
        {
            return _queryMediator.StreamAsync(new StreamNumbersQuery());
        }
    }
}