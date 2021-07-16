using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Registry
{
    internal class MessageRegistry : IMessageRegistry
    {
        private readonly Dictionary<Type, IMessageTypeDescriptor> _descriptors = new();
        private readonly List<HookDescriptor> _postHandlerHooks = new();
        private readonly List<HookDescriptor> _preHandlerHooks = new();

        private readonly HashSet<Assembly> _scannedAssemblies = new();

        public IEnumerator<IMessageTypeDescriptor> GetEnumerator()
        {
            return _descriptors.Values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IMessageTypeDescriptor GetDescriptor(Type messageType)
        {
            if (_descriptors.TryGetValue(messageType, out var messageDescriptor))
            {
                return messageDescriptor;
            }

            if (messageType.BaseType is not null
                && _descriptors.TryGetValue(messageType.BaseType, out var baseMessageDescriptor))
            {
                return baseMessageDescriptor;
            }

            throw new MessageNotRegisteredException(messageType);
        }

        public IMessageTypeDescriptor GetDescriptor<TMessage>()
        {
            return GetDescriptor(typeof(TMessage));
        }

        public void Register(params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                if (_scannedAssemblies.Contains(assembly)) continue;

                foreach (var typeInfo in assembly.DefinedTypes) Register(typeInfo);

                _scannedAssemblies.Add(assembly);
            }

            MatchHooksToMessages();
        }

        public void Register(params Type[] types)
        {
            foreach (var type in types)
            {
                Register(type.GetTypeInfo());
            }
            
            MatchHooksToMessages();
        }

        private void MatchHooksToMessages()
        {
            foreach (var messageDescriptor in _descriptors.Values)
            {
                var postHandleHooks = _postHandlerHooks
                    .Where(h => h.MessageType.IsAssignableFrom(messageDescriptor.MessageType));

                var preHandleHooks = _preHandlerHooks
                    .Where(h => h.MessageType.IsAssignableFrom(messageDescriptor.MessageType));

                foreach (var hookDescriptor in postHandleHooks)
                {
                    ((MessageTypeDescriptor) messageDescriptor).AddPostHandleHookType(hookDescriptor.HookType);
                }

                foreach (var preHandleHook in preHandleHooks)
                {
                    ((MessageTypeDescriptor) messageDescriptor).AddPreHandleHookType(preHandleHook.HookType);
                }
            }
        }

        public void Register(TypeInfo typeInfo)
        {
            var genericImplementedInterfaces = typeInfo
                                               .ImplementedInterfaces
                                               .Where(i => i.IsGenericType);

            foreach (var implementedInterface in genericImplementedInterfaces)
            {
                var messageType = implementedInterface.GetGenericArguments()[0];

                if (implementedInterface.IsAssignableTo(typeof(IMessageHandler)))
                {
                    var descriptor = GetOrAdd(messageType);

                    descriptor.AddHandlerType(typeInfo);
                }
                else if (implementedInterface.IsAssignableTo(typeof(IPostHandleHook)))
                {
                    _postHandlerHooks.Add(new HookDescriptor
                    {
                        HookType = typeInfo, MessageType = messageType
                    });
                }
                else if (implementedInterface.IsAssignableTo(typeof(IPreHandleHook)))
                {
                    _preHandlerHooks.Add(new HookDescriptor
                    {
                        HookType = typeInfo, MessageType = messageType
                    });
                }
            }
        }

        private MessageTypeDescriptor GetOrAdd(Type messageType)
        {
            MessageTypeDescriptor result = default;

            if (_descriptors.ContainsKey(messageType))
            {
                result = _descriptors[messageType] as MessageTypeDescriptor;
            }
            else
            {
                result = new MessageTypeDescriptor(messageType);

                _descriptors[messageType] = result;
            }

            return result;
        }
    }
}