using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions.Extensions
{
    public static class MessageRegistryExtensions
    {
        private static readonly HashSet<Assembly> ScannedAssemblies = new();

        /// <summary>
        ///     Registers messages and hooks from the specified assemblies
        /// </summary>
        /// <param name="registry">The message registry</param>
        /// <param name="assembly">the specified assembly</param>
        public static void Register(this IMessageRegistry registry, Assembly assembly)
        {
            if (ScannedAssemblies.Contains(assembly)) return;

            foreach (var typeInfo in assembly.DefinedTypes)
            {
                Register(registry, typeInfo);
            }

            ScannedAssemblies.Add(assembly);
        }

        /// <summary>
        ///     Register the specified type if it's a message handler or a hook
        /// </summary>
        /// <param name="type">the type to register</param>
        /// <param name="registry">The message registry</param>
        public static void Register(this IMessageRegistry registry, TypeInfo type)
        {
            foreach (var @interface in type.ImplementedInterfaces.Select(i => i.GetGenericTypeDefinition()))
            {
                if (@interface.IsAssignableTo(typeof(IMessageHandler<,>)))
                {
                    registry.RegisterHandler(type);
                }
                else if (@interface.IsAssignableTo(typeof(IPreHandleHook<>)))
                {
                    registry.RegisterPreHandleHook(type);
                }
                else if (@interface.IsAssignableTo(typeof(IPostHandleHook<>)))
                {
                    registry.RegisterPostHandleHook(type);
                }
            }
        }

        public static void RegisterHandler<THandler, TMessage, TMessageResult>(this IMessageRegistry registry)
            where THandler : IMessageHandler<TMessage, TMessageResult>
        {
            registry.RegisterHandler(typeof(THandler));
        }

        public static void RegisterPreHandleHook<THook, TMessage>(this IMessageRegistry registry)
            where THook : IPreHandleHook<TMessage>
        {
            registry.RegisterPreHandleHook(typeof(THook));
        }

        public static void RegisterPostHandleHook<THook, TMessage>(this IMessageRegistry registry)
            where THook : IPostHandleHook<TMessage>
        {
            registry.RegisterPostHandleHook(typeof(THook));
        }
    }
}