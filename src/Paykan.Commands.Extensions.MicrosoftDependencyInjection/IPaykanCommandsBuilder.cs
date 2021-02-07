using System.Reflection;

namespace Paykan.Commands.Extensions.MicrosoftDependencyInjection
{
    public interface IPaykanCommandsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public IPaykanCommandsBuilder Register(Assembly assembly);
    }
}