using System;
using BasicBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BasicBus.Extensions.Microsoft.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBasicBus(this IServiceCollection services,
                                                     Action<ICommandHandlerRegistryBuilder> configureBuilder)
        {
            var basicBusBuilder = new CommandHandlerRegistryBuilder();

            configureBuilder(basicBusBuilder);

            var commandHandlerRegistry = basicBusBuilder.Build();
                
            foreach (var commandDescriptor in commandHandlerRegistry.CommandDescriptors)
            {
                services.AddTransient(commandDescriptor.Handler);
                foreach (var commandDescriptorInterceptor in commandDescriptor.Interceptors)
                {
                    services.AddTransient(commandDescriptorInterceptor);
                }
            }
                
            foreach (var globalInterceptor in commandHandlerRegistry.GlobalInterceptors)
            {
                services.AddTransient(globalInterceptor);
            }


            services.AddTransient<ICommandMediator, CommandMediator>();
            services.AddSingleton<ICommandHandlerRegistry>(f => commandHandlerRegistry);

            return services;
        }
    }
}