using System.Collections.Generic;
using System.Reflection;

namespace LightBus.Queries.Extensions.MicrosoftDependencyInjection
{
    internal class LightBusQueriesBuilder : ILightBusQueriesBuilder
    {
        public LightBusQueriesBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILightBusQueriesBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}