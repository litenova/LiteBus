using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

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
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();

        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox => inbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            }));
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
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddOutboxModule(outbox => outbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            }));
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
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();

        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox => inbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
                postgres.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();
        resolve.Should().Throw<InvalidOperationException>();

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.StartupTasks.Should().NotContain(typeof(PostgreSqlInboxSchemaInitializer));

        manifest.StartupTasks.Should().ContainSingle()
            .Which.Name.Should().Be("InboxObservableMetricsInitializer");

        manifest.BackgroundServices.Should().BeEmpty();
        provider.GetServices<IHostedService>().Should().HaveCount(1);
    }

    [Fact]
    public void DisableSchemaInitialization_OnOutboxStorage_ShouldNotRegisterSchemaInitializer()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddOutboxModule(outbox => outbox.UsePostgreSqlStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
                postgres.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();
        resolve.Should().Throw<InvalidOperationException>();

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.StartupTasks.Should().NotContain(typeof(PostgreSqlOutboxSchemaInitializer));

        manifest.StartupTasks.Should().ContainSingle()
            .Which.Name.Should().Be("OutboxObservableMetricsInitializer");

        manifest.BackgroundServices.Should().BeEmpty();
        provider.GetServices<IHostedService>().Should().HaveCount(1);
    }

    [Fact]
    public void AddInboxInProcessDispatcher_ThenUseAmqpDispatch_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddInboxModule(inbox =>
                    {
                        inbox.UseInMemoryStorage();
                        inbox.UseInProcessDispatch();

                        inbox.UseAmqpDispatch(
                            _ =>
                            {
                            }, new AmqpConnectionOptions { HostName = "localhost" });
                    });
                })
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*Inbox dispatcher is already configured*");
    }

    [Fact]
    public void UseInMemoryStorage_WhenCalledTwiceOnBuilder_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessageModule(_ =>
                    {
                    });

                    registry.AddInboxModule(inbox =>
                    {
                        inbox.UseInMemoryStorage();
                        inbox.UseInMemoryStorage();
                    });
                })
                .BuildServiceProvider();
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*Inbox storage is already configured*");
    }

    [Fact]
    public void DisableIngressConsumer_ShouldNotRegisterIngressHostedService()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseInMemoryStorage();

                inbox.UseAmqpDispatch(
                    _ =>
                    {
                    }, new AmqpConnectionOptions { HostName = "localhost" });

                inbox.UseAmqpIngress(ingress =>
                {
                    ingress.DisableIngressConsumer();

                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = "litebus.inbox.ingress.disabled",
                        Connection = new AmqpConnectionOptions { HostName = "localhost" }
                    });
                });
            });
        });

        using var provider = services.BuildServiceProvider();

        var manifest = provider.GetRequiredService<LiteBusHostManifest>();
        manifest.BackgroundServices.Should().BeEmpty();
        provider.GetServices<IHostedService>().Should().HaveCount(1);
        provider.GetServices<TransportInboxIngressConsumer>().Should().BeEmpty();
    }

    [Fact]
    public void EnableInboxProcessor_WithStorageAndDispatcher_ShouldRegisterProcessorBackgroundService()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.UseInProcessDispatch();
                inbox.EnableInboxProcessor();
            });
        });

        using var provider = services.BuildServiceProvider();
        var manifest = provider.GetRequiredService<LiteBusHostManifest>();

        manifest.BackgroundServices.Should().ContainSingle()
            .Which.Should().Be(typeof(InboxProcessorBackgroundService));

        manifest.StartupTasks.Should().NotContain(typeof(PostgreSqlInboxSchemaInitializer));

        manifest.StartupTasks.Should().ContainSingle()
            .Which.Name.Should().Be("InboxObservableMetricsInitializer");
    }

    /// <summary>
    ///     Confirms inbox and outbox modules can share one registered <see cref="NpgsqlDataSource" /> instance.
    /// </summary>
    [Fact]
    public void SharedDataSource_inbox_and_outbox_modules_should_resolve_same_instance()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        var services = new ServiceCollection();
        services.AddSingleton(_fixture.DataSource);

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox => inbox.UsePostgreSqlStorage(pg =>
            {
                pg.UseDataSource(_fixture.DataSource);
                pg.UseOptions(inboxOptions);
                pg.DisableSchemaInitialization();
            }));

            registry.AddOutboxModule(outbox => outbox.UsePostgreSqlStorage(pg =>
            {
                pg.UseDataSource(_fixture.DataSource);
                pg.UseOptions(outboxOptions);
                pg.DisableSchemaInitialization();
            }));
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<PostgreSqlInboxStoreRegistration>().DataSource
            .Should().BeSameAs(_fixture.DataSource);

        provider.GetRequiredService<PostgreSqlOutboxStoreRegistration>().DataSource
            .Should().BeSameAs(_fixture.DataSource);
    }
}