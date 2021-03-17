using System.Collections.Generic;
using System.Reflection;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    internal class LiteBusMessagingBuilder : ILiteBusMessagingBuilder
    {
        public LiteBusMessagingBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public ILiteBusMessagingBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}