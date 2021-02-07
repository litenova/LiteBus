using System.Diagnostics;
using System.Threading.Tasks;
using Paykan.Commands.Abstractions;

namespace Paykan.WebApi.Crqs
{
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public Task ExecuteAsync(IBaseCommand message)
        {
            Debug.WriteLine("GlobalCommandPostHandleHook executed!");

            return Task.CompletedTask;
        }
    }

    public class CreateColorCommandPostHandleHook : ICommandPostHandleHook<CreateColorCommand>
    {
        public Task ExecuteAsync(CreateColorCommand message)
        {
            Debug.WriteLine("CreateColorCommandPostHandleHook executed!");

            return Task.CompletedTask;
        }
    }
}