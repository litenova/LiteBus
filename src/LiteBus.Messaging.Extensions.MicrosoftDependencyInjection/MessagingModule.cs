using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    internal class MessagingModule : IModule
    {
        private readonly Action<LiteBusMessageBuilder> _builder;

        public MessagingModule(Action<LiteBusMessageBuilder> builder)
        {
            _builder = builder;
        }

        public void Build(IServiceCollection services, IMessageRegistry messageRegistry)
        {
            _builder(new LiteBusMessageBuilder(messageRegistry));
            
        }
    }
}