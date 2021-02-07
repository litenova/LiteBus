using System.Collections.Generic;
using System.Reflection;

namespace Paykan.Commands.Extensions.MicrosoftDependencyInjection
{
    internal class PaykanCommandsBuilder : IPaykanCommandsBuilder
    {
        public PaykanCommandsBuilder()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; }

        public IPaykanCommandsBuilder Register(Assembly assembly)
        {
            Assemblies.Add(assembly);

            return this;
        }
    }
}