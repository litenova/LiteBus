using System.Collections.Generic;
using System.Reflection;

namespace Paykan.Events.Extensions.MicrosoftDependencyInjection
{
    internal class PaykanEventsBuilder : IPaykanEventsBuilder
    {
        public PaykanEventsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public IPaykanEventsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}