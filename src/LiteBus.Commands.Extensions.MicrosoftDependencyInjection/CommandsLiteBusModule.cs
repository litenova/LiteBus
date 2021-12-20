using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    internal class CommandsLiteBusModule : ILiteBusModule
    {
        private readonly Action<LiteBusCommandBuilder> _builder;

        public CommandsLiteBusModule(Action<LiteBusCommandBuilder> builder)
        {
            _builder = builder;
        }

        public void Build(ILiteBusModuleConfiguration configuration)
        {
            _builder(new LiteBusCommandBuilder(configuration.MessageRegistry));

            configuration.Services.TryAddTransient<ICommandMediator, CommandMediator>();
        }
    }
}