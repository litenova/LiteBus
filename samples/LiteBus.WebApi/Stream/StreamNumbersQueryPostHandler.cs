using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Stream;

public class StreamNumbersQueryPostHandler : IQueryPostHandler<StreamNumbersQuery>
{
    public Task PostHandleAsync(IHandleContext<StreamNumbersQuery> context)
    {
        Debug.WriteLine($"{nameof(StreamNumbersQueryPostHandler)} executed!");

        return Task.CompletedTask;
    }
}