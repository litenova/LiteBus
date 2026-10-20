using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LiteBus.Storage.UnitTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL ambient transactional routes fail with <see cref="LiteBusDependencyResolutionException" />
///     when a required registration is missing.
/// </summary>
public sealed class PostgreSqlAmbientTransactionalResolutionTests
{
    /// <summary>
    ///     Confirms resolving the inbox ambient writer without its envelope factory throws the LiteBus resolution exception.
    /// </summary>
    [Fact]
    public void InboxAmbientRoute_WhenEnvelopeFactoryMissing_ShouldThrowDependencyResolutionException()
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
                postgres.EnableAmbientTransactionProvider(TransactionalWriteMode.AllowImmediateCommit);
            }));
        });

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IInboxEnvelopeFactory));
        descriptor.Should().NotBeNull();
        services.Remove(descriptor!);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetService(typeof(ITransactionalInbox));

        act.Should().Throw<LiteBusDependencyResolutionException>()
            .Which.ServiceType.Should().Be<IInboxEnvelopeFactory>();
    }

    /// <summary>
    ///     Confirms resolving the outbox ambient writer without its envelope factory throws the LiteBus resolution exception.
    /// </summary>
    [Fact]
    public void OutboxAmbientRoute_WhenEnvelopeFactoryMissing_ShouldThrowDependencyResolutionException()
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
                postgres.EnableAmbientTransactionProvider(TransactionalWriteMode.AllowImmediateCommit);
            }));
        });

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxEnvelopeFactory));
        descriptor.Should().NotBeNull();
        services.Remove(descriptor!);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetService(typeof(ITransactionalOutbox));

        act.Should().Throw<LiteBusDependencyResolutionException>()
            .Which.ServiceType.Should().Be<IOutboxEnvelopeFactory>();
    }
}
