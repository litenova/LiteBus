using BasicBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BasicBus.Extensions.Microsoft.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static ICommandHandlerRegistryBuilder AddBasicBus(this IServiceCollection services)
        {
            var basicBusBuilder = new CommandHandlerRegistryBuilder();

            services.AddTransient<ICommandMediator, CommandMediator>();
            services.AddSingleton<ICommandHandlerRegistry>(f =>
            {
                var commandHandlerRegistry = basicBusBuilder.Build();
                
                foreach (var commandDescriptor in commandHandlerRegistry.CommandDescriptors)
                {
                    services.AddTransient(commandDescriptor.Handler, commandDescriptor.Handler);
                    foreach (var commandDescriptorInterceptor in commandDescriptor.Interceptors)
                    {
                        services.AddTransient(commandDescriptorInterceptor);
                    }
                }
                
                foreach (var globalInterceptor in commandHandlerRegistry.GlobalInterceptors)
                {
                    services.AddTransient(globalInterceptor);
                }

                return commandHandlerRegistry;
            });

            return basicBusBuilder;
        }
    }
}