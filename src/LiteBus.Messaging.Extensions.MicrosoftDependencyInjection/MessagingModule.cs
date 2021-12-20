using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Mediator;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    internal class MessagingModule : ILiteBusModule
    {
        private readonly Action<LiteBusMessageBuilder> _builder;

        public MessagingModule(Action<LiteBusMessageBuilder> builder)
        {
            _builder = builder;
        }

        public void Build(ILiteBusModuleConfiguration configuration)
        {
            _builder(new LiteBusMessageBuilder(configuration.MessageRegistry));
        }
    }
}