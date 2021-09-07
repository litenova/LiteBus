using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusBuilder : ILiteBusBuilder
    {
        private readonly IMessageRegistry _messageRegistry;
        private readonly HashSet<IModule> _modules = new();
        private readonly IServiceCollection _services;

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
                foreach (var handlerDescriptor in descriptor.Handlers)
                {
                    _services.TryAddTransient(handlerDescriptor.HandlerType);
                }

                foreach (var postHandleDescriptor in descriptor.PostHandlers)
                {
                    _services.TryAddTransient(postHandleDescriptor.PostHandlerType);
                }

                foreach (var preHandleDescriptor in descriptor.PreHandlers)
                {
                    _services.TryAddTransient(preHandleDescriptor.PreHandlerType);
                }
            }
        }
    }
}