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
using LiteBus.Samples.V6.Commands;
using LiteBus.Samples.V6.Diagnostics;
using LiteBus.Samples.V6.Events;
using LiteBus.Samples.V6.Saga;
using LiteBus.Saga;

namespace LiteBus.Samples.V6;

/// <summary>
///     Registers a full LiteBus v6 composition: core mediators, inbox/outbox modules, InMemory storage,
///     explicit dispatch adapters, diagnostic probes, and hosted processor background services.
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

        services.AddLiteBus(builder =>
        {
            var assembly = typeof(ProcessPaymentCommand).Assembly;

            builder.Modules.AddMessageModule(_ => { });
            builder.Modules.AddCommandModule(c => c.RegisterFromAssembly(assembly));
            builder.Modules.AddEventModule(e => e.RegisterFromAssembly(assembly));

            builder.Modules.AddInboxModule(inbox =>
            {
                inbox.Contracts.Register<ProcessPaymentCommand>("payments.process-payment", 1);
                inbox.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 20,
                    LeaseDuration = TimeSpan.FromMinutes(1)
                });
                inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
                inbox.UseInMemoryStorage();
                inbox.UseCommandInboxDispatcher();
                inbox.EnableSaga(registry => registry.MapState<OrderSagaState>("orders.saga.advance"));
                inbox.AddDiagnosticCheck<PaymentSampleDiagnosticCheck>("payments.sample.health");

                // Production PostgreSQL storage (add LiteBus.Inbox.Storage.PostgreSql + Npgsql packages):
                // Register one shared NpgsqlDataSource for inbox + outbox; see docs/Transactional-Messaging-Writes.md.
                // var dataSource = services.BuildServiceProvider().GetRequiredService<NpgsqlDataSource>();
                // inbox.UsePostgreSqlStorage(postgres =>
                // {
                //     postgres.UseDataSource(dataSource);
                //     postgres.EnableAmbientTransactionProvider(); // scoped ITransactionalInbox via IPostgreSqlTransactionProvider
                // });
                // inbox.AddDiagnosticCheck<PostgreSqlInboxSchemaDiagnosticCheck>("inbox.postgresql.schema");

                // Production AMQP ingress (add LiteBus.Inbox.Ingress.Amqp + LiteBus.Transport.Amqp packages):
                // var connectionOptions = new AmqpConnectionOptions
                // {
                //     HostName = configuration["Amqp:HostName"]!,
                //     UserName = configuration["Amqp:UserName"]!,
                //     Password = configuration["Amqp:Password"]!
                // };
                // builder.Modules.Register(new AmqpTransportModule(connectionOptions));
                // inbox.UseAmqpIngress(ingress =>
                // {
                //     ingress.UseOptions(new AmqpInboxIngressOptions
                //     {
                //         QueueName = configuration["Amqp:IngressQueue"]!,
                //         Connection = connectionOptions
                //     });
                // });
            });

            builder.Modules.AddOutboxModule(outbox =>
            {
                outbox.Contracts.Register<PaymentProcessed>("payments.payment-processed", 1);
                outbox.EnableOutboxProcessor(host => host.PollInterval = TimeSpan.FromSeconds(2));
                outbox.UseInMemoryStorage();
                outbox.UseEventOutboxDispatcher();

                // Production PostgreSQL storage (add LiteBus.Outbox.Storage.PostgreSql + Npgsql packages):
                // Share the same NpgsqlDataSource registered for inbox; see docs/Transactional-Messaging-Writes.md.
                // outbox.UsePostgreSqlStorage(postgres =>
                // {
                //     postgres.UseDataSource(dataSource);
                //     postgres.EnableAmbientTransactionProvider(); // scoped ITransactionalOutbox via IPostgreSqlTransactionProvider
                // });
                // outbox.AddDiagnosticCheck<PostgreSqlOutboxSchemaDiagnosticCheck>("outbox.postgresql.schema");

                // Production AMQP dispatch (add LiteBus.Outbox.Dispatch.Amqp + LiteBus.Transport.Amqp packages):
                // outbox.UseAmqpDispatch(
                //     transport => transport.DefaultDestination = configuration["Amqp:OutboxDestination"]!,
                //     connectionOptions);
            });
        });

        return services;
    }
}
