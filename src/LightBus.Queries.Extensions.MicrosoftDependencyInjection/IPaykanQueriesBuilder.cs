using System.Reflection;

namespace LightBus.Queries.Extensions.MicrosoftDependencyInjection
{
    public interface ILightBusQueriesBuilder
    {
        /// <summary>
        ///     Register message handlers and message hooks from the specified assembly
        /// </summary>
        /// <param name="assembly">The assembly to look for message handler and hooks</param>
        public ILightBusQueriesBuilder Register(Assembly assembly);
    }
}