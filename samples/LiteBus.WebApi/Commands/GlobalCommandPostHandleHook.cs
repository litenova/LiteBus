using System.Threading;
using LiteBus.Commands.Abstractions;
using MorseCode.ITask;

namespace LiteBus.WebApi.Commands
{
    public class GlobalCommandPostHandleHook : ICommandPostHandleHook
    {
        public ITask ExecuteAsync(ICommand message, CancellationToken cancellationToken = default)
        {
            throw new System.NotImplementedException();
        }
    }
}