using System;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

internal class MessageModule : IModule
{
    private readonly Action<MessageModuleBuilder> _builder;

    public MessageModule(Action<MessageModuleBuilder> builder)
    {
        _builder = builder;
    }

    public void Build(IModuleConfiguration configuration)
    {
        _builder(new MessageModuleBuilder(configuration.MessageRegistry));
    }
}