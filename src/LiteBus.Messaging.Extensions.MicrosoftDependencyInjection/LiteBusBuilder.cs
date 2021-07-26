using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusBuilder : ILiteBusBuilder
    {
        private readonly IServiceCollection _services;
        private readonly IMessageRegistry _messageRegistry;
        private readonly HashSet<IModule> _modules = new();

        public LiteBusBuilder(IServiceCollection services, IMessageRegistry messageRegistry)
        {
            _services = services;
            _messageRegistry = messageRegistry;
        }
        
        public ILiteBusBuilder AddModule(IModule module)
        {
            _modules.Add(module);

            return this;
        }

        public void Build()
        {
            foreach (var module in _modules)
            {
                module.Build(_services, _messageRegistry);
            }
            
            foreach (var descriptor in _messageRegistry)
            {
                foreach (var handlerType in descriptor.HandlerTypes)
                {
                    _services.TryAddTransient(handlerType);
                }

                foreach (var postHandleDescriptor in descriptor.PostHandleHookDescriptors)
                {
                    _services.TryAddTransient(postHandleDescriptor.HookType);
                }

                foreach (var preHandleDescriptor in descriptor.PreHandleHookDescriptors)
                {
                    _services.TryAddTransient(preHandleDescriptor.HookType);
                }
            }
        }
    }
}