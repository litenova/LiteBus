using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlModuleRegistrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlModuleRegistrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AddPostgreSqlInboxStorage_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInboxStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxLeaseStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxStateStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<PostgreSqlInboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }

    [Fact]
    public void AddPostgreSqlOutboxStorage_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutboxStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxStateStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<PostgreSqlOutboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }

    [Fact]
    public void DisableSchemaInitialization_OnInboxStorage_ShouldNotRegisterSchemaInitializer()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
                postgres.DisableSchemaInitialization();
            });
        });

        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();
        resolve.Should().Throw<InvalidOperationException>();

        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public void DisableSchemaInitialization_OnOutboxStorage_ShouldNotRegisterSchemaInitializer()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
                postgres.DisableSchemaInitialization();
            });
        });

        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();
        resolve.Should().Throw<InvalidOperationException>();

        provider.GetServices<IHostedService>().Should().BeEmpty();
    }

    [Fact]
    public void AddInboxInProcessDispatcher_ThenAddInboxAmqpDispatcher_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(configuration =>
                {
                    configuration.AddInboxModule();
                    configuration.AddInboxInProcessDispatcher();
                    configuration.AddInboxAmqpDispatcher(options => options.Connection.HostName = "localhost");
                })
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IInboxDispatcher*");
    }

    [Fact]
    public void AddInboxAmqpIngress_WhenCalledTwice_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection().AddLiteBus(configuration =>
            {
                configuration.AddInboxAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions { QueueName = "queue.one" });
                });
                configuration.AddInboxAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions { QueueName = "queue.two" });
                });
            });
        };

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddInboxAmqpIngress only once*");
    }

    [Fact]
    public void DisableIngressConsumer_ShouldNotRegisterIngressHostedService()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddInboxModule();
            configuration.AddInMemoryInboxStorage();
            configuration.AddInboxAmqpIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new AmqpInboxIngressOptions
                {
                    QueueName = "litebus.inbox.ingress.disabled",
                    Connection = new LiteBus.Transport.Amqp.AmqpConnectionOptions { HostName = "localhost" }
                });
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().Should().BeEmpty();
        provider.GetServices<AmqpInboxConsumer>().Should().BeEmpty();
    }
}
