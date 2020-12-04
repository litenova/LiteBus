using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BasicBus.Abstractions;
using BasicBus.Internal;

namespace BasicBus
{
    public class CommandHandlerRegistryBuilder : ICommandHandlerRegistryBuilder
    {
        private readonly Type[] _handlersGenericTypeDefinitions =
        {
            typeof(ICommandHandler<>),
            typeof(ICommandHandler<,>),
            typeof(IQueryHandler<,>),
            typeof(IStreamQueryHandler<,>),
        };

        private readonly Type[] _individualInterceptorsTypeDefinitions =
        {
            typeof(ICommandInterceptor<>),
            typeof(ICommandInterceptor<,>),
        };

        private readonly Dictionary<Type, CommandDescriptor> _descriptors;
        private readonly List<Type> _globalInterceptors;

        public CommandHandlerRegistryBuilder()
        {
            _descriptors = new Dictionary<Type, CommandDescriptor>();
            _globalInterceptors = new List<Type>();
        }

        public ICommandHandlerRegistryBuilder RegisterHandlers(Assembly assembly,
                                                        bool registerGlobalInterceptors = true,
                                                        bool registerIndividualInterceptors = true)
        {
            AddHandlers(assembly.DefinedTypes);

            if (registerIndividualInterceptors)
            {
                AddIndividualInterceptors(assembly.DefinedTypes);
            }

            if (registerGlobalInterceptors)
            {
                AddGlobalInterceptors(assembly.DefinedTypes);
            }

            return this;
        }

        public ICommandHandlerRegistry Build()
        {
            return new CommandHandlerRegistry(_descriptors.Values, 
                                              _globalInterceptors);
        }

        private void AddGlobalInterceptors(IEnumerable<TypeInfo> assemblyDefinedTypes)
        {
            foreach (var assemblyDefinedType in assemblyDefinedTypes)
            {
                foreach (var implementedInterface in assemblyDefinedType.ImplementedInterfaces)
                {
                    if (implementedInterface == typeof(ICommandInterceptor))
                    {
                        _globalInterceptors.Add(assemblyDefinedType);
                    }
                }
            }
        }

        private void AddHandlers(IEnumerable<TypeInfo> assemblyDefinedType)
        {
            var descriptors = assemblyDefinedType.SelectMany(typeInfo =>
            {
                return typeInfo.ImplementedInterfaces
                               .Where(implementedInterface => implementedInterface.IsGenericType
                                                              && _handlersGenericTypeDefinitions
                                                                  .Contains(implementedInterface
                                                                                .GetGenericTypeDefinition()))
                               .Select(implementedInterface =>
                                           new CommandDescriptor(implementedInterface.GetGenericArguments()[0],
                                                                 typeInfo));
            });

            foreach (var commandDescriptor in descriptors)
            {
                _descriptors[commandDescriptor.Command] = commandDescriptor;
            }
        }

        private void AddIndividualInterceptors(IEnumerable<TypeInfo> assemblyDefinedTypes)
        {
            foreach (var assemblyDefinedType in assemblyDefinedTypes)
            {
                foreach (var implementedInterface in assemblyDefinedType.ImplementedInterfaces)
                {
                    if (implementedInterface.IsGenericType &&
                        _individualInterceptorsTypeDefinitions.Contains(implementedInterface
                                                                            .GetGenericTypeDefinition()))
                    {
                        var command = implementedInterface.GetGenericArguments()[0];

                        _descriptors[command].AddInterceptor(assemblyDefinedType);
                    }
                }
            }
        }
    }
}