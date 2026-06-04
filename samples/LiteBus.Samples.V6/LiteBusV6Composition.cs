using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Samples.V6.Commands;
using LiteBus.Samples.V6.Events;

namespace LiteBus.Samples.V6;

/// <summary>
///     Registers a full LiteBus v6 composition: core mediators, inbox/outbox modules, InMemory storage,
///     explicit dispatch adapters, and hosted processor background services.
/// </summary>
public static class LiteBusV6Composition
{
    /// <summary>
    ///     Adds LiteBus v6 modules for the payment sample.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configuration">Application configuration (reserved for AMQP/PostgreSQL variants).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLiteBusV6(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;

        services.AddLiteBus(modules =>
        {
            var assembly = typeof(ProcessPaymentCommand).Assembly;

            modules.AddCommandModule(c => c.RegisterFromAssembly(assembly));
            modules.AddEventModule(e => e.RegisterFromAssembly(assembly));

            modules.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 20,
                    LeaseDuration = TimeSpan.FromMinutes(1)
                });
                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
            });
            modules.AddInMemoryInboxStorage();
            modules.AddInboxInProcessDispatcher();

            modules.AddOutboxModule(outbox =>
            {
                outbox.Contracts.Register<PaymentProcessed>("payments.payment-processed", 1);
                outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
            });
            modules.AddInMemoryOutboxStorage();
            modules.AddOutboxInProcessDispatcher();
        });

        return services;
    }
}
