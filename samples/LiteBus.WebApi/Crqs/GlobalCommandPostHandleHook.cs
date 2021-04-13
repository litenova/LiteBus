using System.Diagnostics;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.WebApi.Crqs
{
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(IBaseCommand message)
        {
            Debug.WriteLine("GlobalCommandPostHandleHook executed!");

            return Task.CompletedTask;
        }
    }
    
    public class GlobalCommandPreHandleHook : ICommandPreHandleHook
    {
        public Task ExecuteAsync(IBaseCommand message)
        {
            Debug.WriteLine("GlobalCommandPreHandleHook executed!");

            return Task.CompletedTask;
        }
    }
}