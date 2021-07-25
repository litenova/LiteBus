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
        private readonly Dictionary<Type, IMessageDescriptor> _descriptors = new();
        private readonly List<HookDescriptor> _postHandlerHooks = new();
        private readonly List<HookDescriptor> _preHandlerHooks = new();
        private event EventHandler<MessageDescriptor> NewMessageDescriptorCreated;

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
                    ((MessageDescriptor) messageDescriptor).AddPostHandleHookType(hookDescriptor.HookType);
                }

                foreach (var preHandleHook in preHandleHooks)
                {
                    ((MessageDescriptor) messageDescriptor).AddPreHandleHookType(preHandleHook.HookType);
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
                    var descriptor = GetOrAddMessageDescriptor(messageType);

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

        public IEnumerator<IMessageDescriptor> GetEnumerator() => _descriptors.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void RegisterHandler(Type type)
        {
            var messageType = type.GetGenericArguments()[0];

            var GetOrAddMessageDescriptor();
        }

        public void RegisterPreHandleHook(Type type)
        {
            throw new NotImplementedException();
        }

        public void RegisterPostHandleHook(Type type)
        {
            throw new NotImplementedException();
        }

        private MessageDescriptor GetOrAddMessageDescriptor(Type messageType)
        {
            MessageDescriptor messageDescriptor = default;

            if (_descriptors.ContainsKey(messageType))
            {
                messageDescriptor = _descriptors[messageType] as MessageDescriptor;
            }
            else
            {
                messageDescriptor = new MessageDescriptor(messageType);

                _descriptors[messageType] = messageDescriptor;
                
                NewMessageDescriptorCreated?.Invoke(this, messageDescriptor);
            }

            return messageDescriptor;
        }
    }
}