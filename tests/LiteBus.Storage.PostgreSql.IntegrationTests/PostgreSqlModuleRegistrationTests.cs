using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Transport;
using LiteBus.Transport.Amqp;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
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
        services.AddLiteBus(modules =>
        {
            modules.AddInboxModule();
            modules.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInboxStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxLeaseStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxStateWriter>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxDeadLetterStore>().Should().BeOfType<PostgreSqlInboxStore>();
        provider.GetRequiredService<IInboxRetentionStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<IInboxDiagnosticsStore>().Should().BeSameAs(provider.GetRequiredService<IInboxStore>());
        provider.GetRequiredService<PostgreSqlInboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }

    [Fact]
    public void AddPostgreSqlOutboxStorage_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(modules =>
        {
            modules.AddOutboxModule();
            modules.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutboxStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxLeaseStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxStateWriter>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxDeadLetterStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxRetentionStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<IOutboxDiagnosticsStore>().Should().BeSameAs(provider.GetRequiredService<IOutboxStore>());
        provider.GetRequiredService<PostgreSqlOutboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }

    [Fact]
    public void DisableSchemaInitialization_OnInboxStorage_ShouldNotRegisterSchemaInitializer()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(modules =>
        {
            modules.AddInboxModule();
            modules.AddPostgreSqlInboxStorage(postgres =>
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
        services.AddLiteBus(modules =>
        {
            modules.AddOutboxModule();
            modules.AddPostgreSqlOutboxStorage(postgres =>
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
    public void AddInboxInProcessDispatcher_ThenUseTransport_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(modules =>
                {
                    modules.AddInboxModule(inbox =>
                    {
                        inbox.UseInProcessDispatcher();
                        inbox.UseTransport(
                            _ => { },
                            new AmqpTransportModule(new AmqpConnectionOptions { HostName = "localhost" }));
                    });
                })
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*IInboxDispatcher*");
    }

    [Fact]
    public void AddInboxAmqpIngress_WhenCalledTwice_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection().AddLiteBus(modules =>
            {
                modules.AddInboxModule();
                modules.AddInboxAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions { QueueName = "queue.one" });
                });
                modules.AddInboxAmqpIngress(ingress =>
                {
                    ingress.UseOptions(new AmqpInboxIngressOptions { QueueName = "queue.two" });
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*AddInboxAmqpIngress only once*");
    }

    [Fact]
    public void DisableIngressConsumer_ShouldNotRegisterIngressHostedService()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(modules =>
        {
            modules.AddInboxModule();
            modules.AddInboxModule(inbox => inbox.UseInMemoryStorage());
            modules.AddInboxAmqpIngress(ingress =>
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
