using LiteBus.Commands;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Commands;
using LiteBus.Inbox.Extensions.Microsoft.Hosting;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.Events;
using LiteBus.Outbox.Extensions.Microsoft.Hosting;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Samples.V6.Commands;
using LiteBus.Samples.V6.Events;

namespace LiteBus.Samples.V6;

/// <summary>
///     Registers a full LiteBus v6 composition: core mediators, inbox/outbox modules, InMemory storage,
///     explicit dispatch adapters, and processor hosting.
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

        services.AddLiteBus(lb =>
        {
            var assembly = typeof(ProcessPaymentCommand).Assembly;

            lb.AddCommandModule(c => c.RegisterFromAssembly(assembly));
            lb.AddEventModule(e => e.RegisterFromAssembly(assembly));

            lb.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 20,
                    LeaseDuration = TimeSpan.FromMinutes(1)
                });
            });
            lb.AddInMemoryInboxStorage();
            lb.AddInboxCommandDispatcher();
            lb.AddInboxProcessorHosting(host => host.PollInterval = TimeSpan.FromSeconds(2));

            lb.AddOutboxModule(outbox =>
            {
                outbox.Contracts.Register<PaymentProcessed>("payments.payment-processed", 1);
            });
            lb.AddInMemoryOutboxStorage();
            lb.AddOutboxEventDispatcher();
            lb.AddOutboxProcessorHosting(host => host.PollInterval = TimeSpan.FromSeconds(2));
        });

        return services;
    }
}
