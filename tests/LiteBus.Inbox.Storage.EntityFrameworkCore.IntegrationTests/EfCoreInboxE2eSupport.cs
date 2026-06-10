using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

internal static class EfCoreInboxE2eSupport
{
    internal static readonly DateTimeOffset BaseTime = new(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);

    internal static EfCoreInboxStoreOptions CreateStoreOptions(string tableName)
    {
        return new EfCoreInboxStoreOptions
        {
            SchemaName = EfCorePostgreSqlTestInfrastructure.SchemaName,
            TableName = tableName
        };
    }

    internal static async Task EnsureInboxTableAsync(string connectionString, EfCoreInboxStoreOptions storeOptions)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgreSqlInboxSchema.EnsureAsync(
            dataSource,
            new PostgreSqlInboxStoreOptions
            {
                SchemaName = storeOptions.SchemaName,
                TableName = storeOptions.TableName,
                ValidateSchemaCreationOnStartup = false
            });
    }

    internal static ServiceProvider BuildProvider<TDbContext>(
        string connectionString,
        EfCoreInboxStoreOptions storeOptions,
        InboxE2eComposition composition)
        where TDbContext : EfCoreInboxE2eDbContext
    {
        var services = new ServiceCollection();

        if (composition.Recorder is not null)
        {
            services.AddSingleton(composition.Recorder);
        }

        services.AddScoped<TDbContext>(_ =>
        {
            var builder = new DbContextOptionsBuilder<TDbContext>()
                .UseNpgsql(EfCorePostgreSqlTestInfrastructure.CreateScopedConnectionString(connectionString, storeOptions));
            return (TDbContext)Activator.CreateInstance(typeof(TDbContext), builder.Options, storeOptions)!;
        });

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ => { });
            registry.AddCommandModule(module =>
            {
                if (composition.RegisterShipHandler)
                {
                    module.Register<ShipOrderCommand>();
                    module.Register<ShipOrderCommandHandler>();
                }

                if (composition.RegisterFaultyHandler)
                {
                    module.Register<FaultyCommand>();
                    module.Register<FaultyCommandHandler>();
                }
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseEfCoreStorage(builder =>
                {
                    builder.UseDbContext<TDbContext>();
                    builder.UseOptions(storeOptions);
                });

                if (composition.RegisterShipHandler)
                {
                    inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                }

                if (composition.RegisterFaultyHandler)
                {
                    inbox.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
                }

                inbox.UseProcessorOptions(new InboxProcessorOptions
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
                inbox.UseCommandInboxDispatcher();
            });
        });

        if (composition.Clock is not null)
        {
            services.AddSingleton<TimeProvider>(composition.Clock);
        }

        return services.BuildServiceProvider();
    }
}

internal sealed class InboxE2eComposition
{
    public CommandRecorder? Recorder { get; init; }

    public TimeProvider? Clock { get; init; }

    public bool RegisterShipHandler { get; init; } = true;

    public bool RegisterFaultyHandler { get; init; }

    public int MaxAttempts { get; init; } = 5;

    public TimeSpan? InitialDelay { get; init; }

    public string LeaseOwner { get; init; } = "efcore-inbox-e2e";
}
