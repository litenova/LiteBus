using System;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

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