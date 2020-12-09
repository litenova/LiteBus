using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Paykan.Abstractions;
using Paykan.Abstractions.Interceptors;
using Paykan.Internal;

namespace Paykan.Builders
{
    public class MessageRegistryBuilder : IMessageRegistryBuilder
    {
        private readonly MessageRegistry _messageRegistry = new MessageRegistry();
        private readonly List<HookDescriptor> _postHandlerHooks = new List<HookDescriptor>();

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
        
        public IMessageRegistryBuilder RegisterPostHandlerHooks(Assembly assembly)
        {
            foreach (var assemblyDefinedType in assembly.DefinedTypes)
            {
                foreach (var implementedInterface in assemblyDefinedType.ImplementedInterfaces)
                {
                    if (implementedInterface.IsGenericType &&
                        implementedInterface.GetGenericTypeDefinition()
                                            .IsAssignableTo(typeof(IPostHandleHook<>)))
                    {
                        var messageType = implementedInterface.GetGenericArguments()[0];
                        
                        _postHandlerHooks.Add(new HookDescriptor
                        {
                            HookType = assemblyDefinedType,
                            MessageType = messageType
                        });
                    }
                }
            }

            return this;
        }

        public IMessageRegistry Build()
        {
            foreach (var keyValuePair in _messageRegistry)
            {
                var hooks = _postHandlerHooks
                    .Where(h => h.MessageType.IsAssignableFrom(keyValuePair.Key));

                foreach (var hookDescriptor in hooks)
                {
                    ((MessageDescriptor)keyValuePair.Value).AddPostHandleHookType(hookDescriptor.HookType);
                }
            }

            return _messageRegistry;
        }
    }
}