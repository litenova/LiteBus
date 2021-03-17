using System.Reflection;

namespace LightBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public interface ILightBusMessagingBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILightBusMessagingBuilder Register(Assembly assembly);
    }
}