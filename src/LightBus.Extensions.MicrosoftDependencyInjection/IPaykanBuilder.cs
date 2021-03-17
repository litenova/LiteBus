using System.Reflection;

namespace LightBus.Extensions.MicrosoftDependencyInjection
{
    public interface ILightBusBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILightBusBuilder Register(Assembly assembly);
    }
}