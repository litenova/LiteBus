using System;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    internal class CommandsModule : IModule
    {
        private readonly Action<LiteBusCommandBuilder> _builder;

        public CommandsModule(Action<LiteBusCommandBuilder> builder)
        {
            _builder = builder;
        }

        public void Build(IServiceCollection services, IMessageRegistry messageRegistry)
        {
            _builder(new LiteBusCommandBuilder(messageRegistry));

            services.TryAddTransient<ICommandMediator, CommandMediator>();
        }
    }
}