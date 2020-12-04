using System.Reflection;
using Paykan.Abstractions;
using Paykan.Internal;

namespace Paykan.Builders
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
                        MessageDescriptor descriptor;
                        
                        if (_messageRegistry.ContainsKey(messageType))
                        {
                            descriptor = (MessageDescriptor)_messageRegistry[messageType];
                            
                            descriptor.AddHandlerType(assemblyDefinedType);
                        }
                        else
                        {
                            descriptor = new MessageDescriptor(messageType);
                            descriptor.AddHandlerType(assemblyDefinedType);

                            _messageRegistry[messageType] = descriptor;
                        }
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