using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Saga.InboxIntegration;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.Runtime.Composition;

/// <summary>
///     Registers a minimal inbox, outbox, and saga composition used by composition smoke tests.
/// </summary>
public static class V6CompositionSmokeRegistration
{
    /// <summary>
    ///     Adds LiteBus modules for inbox/outbox composition smoke tests.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddV6CompositionSmoke(this IServiceCollection services)
    {
        services.AddLiteBus(builder =>
        {
            var assembly = typeof(AdvanceOrderSagaCommandHandler).Assembly;

            builder.Modules.AddMessageModule(_ =>
            {
            });

            builder.Modules.AddCommandModule(command => command.RegisterFromAssembly(assembly));

            builder.Modules.AddEventModule(events => events.RegisterFromAssembly(assembly));

            builder.Modules.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance");

                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 20,
                    LeaseDuration = TimeSpan.FromMinutes(1)
                });

                inbox.UseInMemoryStorage();
                inbox.UseInProcessDispatch();
                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
                inbox.EnableSaga(saga =>
                {
                    saga.MapState<OrderSagaState>("orders.saga.advance");
                    saga.UseInMemoryStorage();
                });
            });

            builder.Modules.AddOutboxModule(outbox =>
            {
                outbox.UseInMemoryStorage();
                outbox.UseInProcessDispatch();
                outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
            });
        });

        return services;
    }
}
