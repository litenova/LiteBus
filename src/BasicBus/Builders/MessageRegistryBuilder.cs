using System.Reflection;
using BasicBus.Abstractions;
using BasicBus.Internal;

namespace BasicBus.Builders
{
    public class MessageRegistryBuilder : IMessageRegistryBuilder
    {
        private readonly MessageRegistry _messageRegistry = new MessageRegistry();

        public IMessageRegistryBuilder RegisterHandlers(Assembly assembly)
        {
            foreach (var assemblyDefinedType in assembly.DefinedTypes)
            {
                foreach (var implementedInterface in assemblyDefinedType.ImplementedInterfaces)
                {
                    if (implementedInterface.IsGenericType &&
                        implementedInterface.GetGenericTypeDefinition()
                                            .IsAssignableTo(typeof(IMessageHandler<,>)))
                    {
                        var messageType = implementedInterface.GetGenericArguments()[0];
                        
                        _messageRegistry[messageType] = 
                            new MessageDescriptor(messageType, assemblyDefinedType);
                    }
                }
            }

            return this;
        }

        public IMessageRegistry Build()
        {
            return _messageRegistry;
        }
    }
}