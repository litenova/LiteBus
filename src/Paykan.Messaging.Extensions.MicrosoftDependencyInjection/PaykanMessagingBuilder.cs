using System.Collections.Generic;
using System.Reflection;

namespace Paykan.Messaging.Extensions.MicrosoftDependencyInjection
{
    internal class PaykanMessagingBuilder : IPaykanMessagingBuilder
    {
        public PaykanMessagingBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public IPaykanMessagingBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}