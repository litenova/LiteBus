using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Queries.Abstractions;

namespace LiteBus.WebApi.Queries
{
    public class GlobalQueryPostHandleAsyncHook : IQueryPostHandleAsyncHook
    {
        public Task ExecuteAsync(IQueryBase message, CancellationToken cancellationToken)
        {
            Debug.WriteLine($"{nameof(GlobalQueryPostHandleAsyncHook)} executed!");

            return Task.CompletedTask;
        }
    }
}