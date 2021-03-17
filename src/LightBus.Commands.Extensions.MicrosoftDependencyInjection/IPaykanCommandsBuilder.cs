using System.Reflection;

namespace LightBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public interface ILightBusCommandsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILightBusCommandsBuilder Register(Assembly assembly);
    }
}