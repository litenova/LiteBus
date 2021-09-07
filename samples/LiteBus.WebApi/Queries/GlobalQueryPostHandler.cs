using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Queries
{
    public class GlobalQueryPostHandler : IQueryPostHandler
    {
        public Task PostHandleAsync(IHandleContext<IQueryBase> context)
        {
            Debug.WriteLine($"{nameof(GlobalQueryPostHandler)} executed!");

            return Task.CompletedTask;
        }
    }
}