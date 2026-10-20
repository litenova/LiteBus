using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Stores;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.UnitTests.PostgreSql;

/// <summary>
///     Verifies PostgreSQL module data source ownership and work signal disposal without a database.
/// </summary>
public sealed class PostgreSqlDataSourceOwnershipTests
{
    /// <summary>
    ///     Confirms disposing the provider disposes an outbox data source the module created.
    /// </summary>
    [Fact]
    public void OutboxModule_WhenConnectionStringSupplied_ShouldDisposeOwnedDataSource()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddOutbox(outbox => outbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseConnectionString("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1");
                postgres.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOutboxStore>();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        provider.Dispose();

        var act = () =>
        {
            using var connection = dataSource.OpenConnection();
        };

        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    ///     Confirms disposing the provider leaves an application supplied outbox data source usable.
    /// </summary>
    [Fact]
    public void OutboxModule_WhenDataSourceSupplied_ShouldNotDisposeDataSource()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1");
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddOutbox(outbox => outbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(dataSource);
                postgres.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOutboxStore>();

        provider.Dispose();

        // Port 1 refuses the connection, so opening fails fast; what matters is that it fails for a reason other than
        // disposal, which proves the provider left the application's data source alone.
        var open = () =>
        {
            using var connection = dataSource.OpenConnection();
        };

        open.Should().NotThrow<ObjectDisposedException>();
    }

    /// <summary>
    ///     Confirms disposing the provider disposes an inbox data source the module created.
    /// </summary>
    [Fact]
    public void InboxModule_WhenConnectionStringSupplied_ShouldDisposeOwnedDataSource()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddInbox(inbox => inbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseConnectionString("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1");
                postgres.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IInboxStore>();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        provider.Dispose();

        var act = () =>
        {
            using var connection = dataSource.OpenConnection();
        };

        act.Should().Throw<ObjectDisposedException>();
    }

    /// <summary>
    ///     Confirms disposing the provider leaves an application supplied inbox data source usable.
    /// </summary>
    [Fact]
    public void InboxModule_WhenDataSourceSupplied_ShouldNotDisposeDataSource()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1");
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddInbox(inbox => inbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(dataSource);
                postgres.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IInboxStore>();

        provider.Dispose();

        // Port 1 refuses the connection, so opening fails fast; what matters is that it fails for a reason other than
        // disposal, which proves the provider left the application's data source alone.
        var open = () =>
        {
            using var connection = dataSource.OpenConnection();
        };

        open.Should().NotThrow<ObjectDisposedException>();
    }

    /// <summary>
    ///     Confirms work signal disposal can run more than once without throwing.
    /// </summary>
    [Fact]
    public async Task WorkSignal_DisposeAsync_ShouldBeIdempotent()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1");
        var signal = new PostgreSqlWorkSignal(dataSource, "litebus_test_work");
        await using var signalScope = signal.ConfigureAwait(false);

        await signal.DisposeAsync().ConfigureAwait(false);
        await signal.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     Confirms waiting after disposal falls back to the poll interval instead of opening a connection.
    /// </summary>
    [Fact]
    public async Task WorkSignal_WaitForWorkOrDelayAsync_AfterDisposal_ShouldCompleteWithoutThrowing()
    {
        using var dataSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Database=x;Username=u;Password=p;Timeout=1");
        var signal = new PostgreSqlWorkSignal(dataSource, "litebus_test_work");
        await using var signalScope = signal.ConfigureAwait(false);

        await signal.DisposeAsync().ConfigureAwait(false);
        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
    }
}
