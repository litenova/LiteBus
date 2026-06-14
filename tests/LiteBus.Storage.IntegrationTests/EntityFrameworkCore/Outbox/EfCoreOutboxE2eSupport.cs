using LiteBus.Outbox;
using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

internal static class EfCoreOutboxE2eSupport
{
    internal static readonly DateTimeOffset BaseTime = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    internal static EntityFrameworkCoreOutboxStoreOptions CreateStoreOptions(string tableName)
    {
        return new EntityFrameworkCoreOutboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = tableName
        };
    }

    internal static async Task EnsureOutboxTableAsync(string connectionString, EntityFrameworkCoreOutboxStoreOptions storeOptions)
    {
         var dataSource = NpgsqlDataSource.Create(connectionString);
         await using (dataSource.ConfigureAwait(false))
         {

        await PostgreSqlOutboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlOutboxStoreOptions
            {
                SchemaName = storeOptions.SchemaName,
                TableName = storeOptions.TableName,
                ValidateSchemaCreationOnStartup = false
            }).ConfigureAwait(false);
        }
    }

    internal static ServiceProvider BuildProvider<TDbContext>(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions storeOptions,
        OutboxE2eComposition composition)
        where TDbContext : EfCoreOutboxE2eDbContext
    {
        var services = new ServiceCollection();

        if (composition.Recorder is not null)
        {
            services.AddSingleton(composition.Recorder);
        }

        if (composition.UseFailingDispatcher)
        {
            services.AddSingleton<IOutboxDispatcher, AlwaysFailingOutboxDispatcher>();
        }

        services.AddScoped<TDbContext>(_ =>
        {
            var builder = new DbContextOptionsBuilder<TDbContext>()
                .UseNpgsql(EfCorePostgreSqlTestInfrastructure.CreateScopedConnectionString(connectionString, storeOptions));

            return (TDbContext) Activator.CreateInstance(typeof(TDbContext), builder.Options, storeOptions)!;
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddEventModule(module =>
            {
                module.Register<OrderSubmittedEventHandler>();
            });

            registry.AddOutboxModule(outbox =>
            {
                outbox.UseEntityFrameworkCoreStorage(builder =>
                {
                    builder.UseDbContext<TDbContext>();
                    builder.UseOptions(storeOptions);
                });

                outbox.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = composition.LeaseOwner,
                    Retry = new RetryOptions
                    {
                        MaxAttempts = composition.MaxAttempts,
                        InitialDelay = composition.InitialDelay ?? TimeSpan.Zero,
                        UseJitter = false
                    }
                });

                if (!composition.UseFailingDispatcher)
                {
                    outbox.UseInProcessDispatch();
                }
            });
        });

        if (composition.Clock is not null)
        {
            services.AddSingleton(composition.Clock);
        }

        return services.BuildServiceProvider();
    }
}

internal sealed class OutboxE2eComposition
{
    public EventRecorder? Recorder { get; init; }

    public TimeProvider? Clock { get; init; }

    public bool UseFailingDispatcher { get; init; }

    public int MaxAttempts { get; init; } = 5;

    public TimeSpan? InitialDelay { get; init; }

    public string LeaseOwner { get; init; } = "efcore-outbox-e2e";
}
