using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Stream;

public class StreamNumbersQueryPreHandler : IQueryPreHandler<StreamNumbersQuery>
{
    public Task PreHandleAsync(IHandleContext<StreamNumbersQuery> context)
    {
        Debug.WriteLine($"{nameof(StreamNumbersQueryPreHandler)} executed!");

        return Task.CompletedTask;
    }
}