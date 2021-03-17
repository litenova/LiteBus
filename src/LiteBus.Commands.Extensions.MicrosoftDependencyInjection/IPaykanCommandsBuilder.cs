using System.Reflection;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public interface ILiteBusCommandsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILiteBusCommandsBuilder Register(Assembly assembly);
    }
}