using System.Collections.Generic;
using System.Reflection;

namespace LightBus.Events.Extensions.MicrosoftDependencyInjection
{
    internal class LightBusEventsBuilder : ILightBusEventsBuilder
    {
        public LightBusEventsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILightBusEventsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}