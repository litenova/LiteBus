using System.Reflection;

namespace LightBus.Events.Extensions.MicrosoftDependencyInjection
{
    public interface ILightBusEventsBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILightBusEventsBuilder Register(Assembly assembly);
    }
}