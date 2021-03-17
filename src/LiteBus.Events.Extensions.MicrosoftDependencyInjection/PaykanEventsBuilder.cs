using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusEventsBuilder : ILiteBusEventsBuilder
    {
        public LiteBusEventsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusEventsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}