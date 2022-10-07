using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection;

internal class CommandModule : IModule
{
    private readonly Action<CommandModuleBuilder> _builder;

    public CommandModule(Action<CommandModuleBuilder> builder)
    {
        _builder = builder;
    }

    public void Build(ILiteBusModuleConfiguration configuration)
    {
        _builder(new CommandModuleBuilder(configuration.MessageRegistry));

        configuration.Services.TryAddTransient<ICommandMediator, CommandMediator>();
    }
}