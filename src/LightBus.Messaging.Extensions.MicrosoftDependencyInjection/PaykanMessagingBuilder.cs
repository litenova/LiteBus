using System.Collections.Generic;
using System.Reflection;

namespace LightBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    internal class LightBusMessagingBuilder : ILightBusMessagingBuilder
    {
        public LightBusMessagingBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILightBusMessagingBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}