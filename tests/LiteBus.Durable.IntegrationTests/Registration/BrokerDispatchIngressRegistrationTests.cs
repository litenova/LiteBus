using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Dispatch.AwsSqs;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Ingress.AwsSqs;
using LiteBus.Inbox.Ingress.AzureServiceBus;
using LiteBus.Inbox.Ingress.InMemory;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Outbox.Dispatch.Amqp;
using LiteBus.Outbox.Dispatch.AwsSqs;
using LiteBus.Outbox.Dispatch.AzureServiceBus;
using LiteBus.Outbox.Dispatch.InMemory;
using LiteBus.Outbox.Dispatch.Kafka;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.AwsSqs;
using LiteBus.Transport.AzureServiceBus;
using LiteBus.Transport.InMemory;
using LiteBus.Transport.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Durable.IntegrationTests.Registration;

/// <summary>
///     Verifies every broker dispatch and ingress extension resolves transport services without a live broker.
/// </summary>
public sealed class BrokerDispatchIngressRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies inbox broker dispatch extensions register <see cref="TransportInboxDispatcher" /> and transport services.
    /// </summary>
    /// <param name="transportModule">The root transport module required by the dispatcher.</param>
    /// <param name="configure">The inbox module configuration action.</param>
    [Theory]
    [MemberData(nameof(InboxDispatchConfigurations))]
    public void InboxDispatchExtensions_ShouldRegisterTransportDispatcher(
        IModule transportModule,
        Action<InboxModuleBuilder> configure)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(transportModule);
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    configure(inbox);
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<TransportInboxDispatcher>();
        provider.GetRequiredService<ITransportPublisher>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies outbox broker dispatch extensions register <see cref="TransportOutboxDispatcher" /> and transport
    ///     services.
    /// </summary>
    /// <param name="transportModule">The root transport module required by the dispatcher.</param>
    /// <param name="configure">The outbox module configuration action.</param>
    [Theory]
    [MemberData(nameof(OutboxDispatchConfigurations))]
    public void OutboxDispatchExtensions_ShouldRegisterTransportDispatcher(
        IModule transportModule,
        Action<OutboxModuleBuilder> configure)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(transportModule);
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    configure(outbox);
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<TransportOutboxDispatcher>();
        provider.GetRequiredService<ITransportPublisher>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies broker ingress extensions register the shared ingress handler and consumer types.
    /// </summary>
    /// <param name="transportModule">The root transport module required by ingress.</param>
    /// <param name="configure">The inbox module configuration action.</param>
    [Theory]
    [MemberData(nameof(InboxIngressConfigurations))]
    public void InboxIngressExtensions_ShouldRegisterIngressServices(
        IModule transportModule,
        Action<InboxModuleBuilder> configure)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(transportModule);
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    configure(inbox);
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<TransportInboxIngressHandler>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies every broker builder preserves shared safety settings while mapping only its native consumer knobs.
    /// </summary>
    /// <param name="transportModule">The root transport module required by ingress.</param>
    /// <param name="configure">The inbox module configuration action.</param>
    /// <param name="assertOptions">The adapter-specific runtime mapping assertion.</param>
    [Theory]
    [MemberData(nameof(InboxIngressOptionConfigurations))]
    public void InboxIngressOptions_ShouldPreserveSafetyAndNativeConsumerSettings(
        IModule transportModule,
        Action<InboxModuleBuilder> configure,
        Action<TransportInboxIngressOptions> assertOptions)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(transportModule);
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    configure(inbox);
                });
            })
            .BuildServiceProvider();

        assertOptions(provider.GetRequiredService<TransportInboxIngressOptions>());
    }

    /// <summary>
    ///     Verifies an SQS receive batch outside the documented 1 through 10 range fails during module composition.
    /// </summary>
    [Fact]
    public void AwsSqsIngress_WithInvalidReceiveBatchSize_ShouldRejectComposition()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new AwsSqsTransportModule(new AwsSqsTransportOptions
                {
                    ServiceUrl = "http://localhost:4566"
                }));
                registry.AddMessaging(_ =>
                {
                });
                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseAwsSqsIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();
                        ingress.UseOptions(new AwsSqsInboxIngressOptions
                        {
                            Destination = "http://localhost:4566/000000000000/commands",
                            ReceiveBatchSize = 11
                        });
                    });
                });
            });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies a non-positive Azure callback limit fails during module composition.
    /// </summary>
    [Fact]
    public void AzureServiceBusIngress_WithInvalidMaxConcurrentCalls_ShouldRejectComposition()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
                {
                    ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b"
                }));
                registry.AddMessaging(_ =>
                {
                });
                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseAzureServiceBusIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();
                        ingress.UseOptions(new AzureServiceBusInboxIngressOptions
                        {
                            Destination = "commands",
                            MaxConcurrentCalls = 0
                        });
                    });
                });
            });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    ///     Verifies a non-positive provider-neutral admission limit fails during module composition.
    /// </summary>
    [Fact]
    public void InMemoryIngress_WithInvalidMaxInFlightMessages_ShouldRejectComposition()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new InMemoryTransportModule());
                registry.AddMessaging(_ =>
                {
                });
                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();
                        ingress.UseOptions(new InMemoryInboxIngressOptions
                        {
                            Destination = "commands",
                            Safety = new TransportInboxIngressSafetyOptions
                            {
                                MaxInFlightMessages = 0
                            }
                        });
                    });
                });
            });

        act.Should()
            .Throw<LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException>()
            .WithMessage("*MaxInFlightMessages*");
    }

    /// <summary>
    ///     Verifies ingress and dispatch share an in-memory transport registered at the root.
    /// </summary>
    [Fact]
    public void InMemoryIngress_WithRegisteredTransport_ShouldShareDispatchTransport()
    {
        var ingressConfigurationCount = 0;

        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new InMemoryTransportModule());
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryDispatch();
                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingressConfigurationCount++;
                        ingress.DisableIngressConsumer();
                        ingress.UseOptions(new InMemoryInboxIngressOptions { Destination = "commands" });
                    });
                });
            })
            .BuildServiceProvider();

        ingressConfigurationCount.Should().Be(1);
        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<TransportInboxDispatcher>();
        provider.GetRequiredService<TransportInboxIngressHandler>().Should().NotBeNull();
        provider.GetServices<IMessageConsumer>().Should().ContainSingle();
    }

    /// <summary>
    ///     Verifies shared ingress transport mode requires the matching transport module in the graph.
    /// </summary>
    [Fact]
    public void InMemoryIngress_WithMissingRegisteredTransport_ShouldRejectConfiguration()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();
                        ingress.UseOptions(new InMemoryInboxIngressOptions { Destination = "commands" });
                    });
                });
            })
            .BuildServiceProvider();

        act.Should()
            .Throw<LiteBus.Runtime.Abstractions.Exceptions.LiteBusConfigurationException>()
            .WithMessage("*InMemoryInboxIngressModule*requires*InMemoryTransportModule*");
    }

    /// <summary>
    ///     Verifies one transport module can serve inbox dispatch, outbox dispatch, and inbox ingress.
    /// </summary>
    [Fact]
    public void RegisteredTransport_ShouldSupportCombinedDurableComposition()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.Modules.Register(new InMemoryTransportModule());

                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.UseInMemoryDispatch();
                    inbox.UseInMemoryIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();
                        ingress.UseOptions(new InMemoryInboxIngressOptions { Destination = "commands" });
                    });
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    outbox.UseInMemoryDispatch();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<TransportInboxDispatcher>();
        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<TransportOutboxDispatcher>();
        provider.GetRequiredService<TransportInboxIngressHandler>().Should().NotBeNull();
        provider.GetServices<ITransportPublisher>().Should().ContainSingle();
        provider.GetServices<IMessageConsumer>().Should().ContainSingle();
    }

    /// <summary>
    ///     Gets inbox dispatch configuration cases for each broker package.
    /// </summary>
    /// <returns>The inbox dispatch configuration data.</returns>
    public static TheoryData<IModule, Action<InboxModuleBuilder>> InboxDispatchConfigurations()
    {
        var data = new TheoryData<IModule, Action<InboxModuleBuilder>>();
        data.Add(new InMemoryTransportModule(), inbox => inbox.UseInMemoryDispatch());
        data.Add(new AmqpTransportModule(new AmqpConnectionOptions { HostName = "localhost" }), inbox => inbox.UseAmqpDispatch(_ =>
        {
        }));
        data.Add(new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b"
        }), inbox => inbox.UseAzureServiceBusDispatch(_ =>
        {
        }));
        data.Add(new AwsSqsTransportModule(new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }), inbox => inbox.UseAwsSqsDispatch(_ =>
        {
        }));
        data.Add(new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" }), inbox => inbox.UseKafkaDispatch(_ =>
        {
        }));
        return data;
    }

    /// <summary>
    ///     Gets outbox dispatch configuration cases for each broker package.
    /// </summary>
    /// <returns>The outbox dispatch configuration data.</returns>
    public static TheoryData<IModule, Action<OutboxModuleBuilder>> OutboxDispatchConfigurations()
    {
        var data = new TheoryData<IModule, Action<OutboxModuleBuilder>>();
        data.Add(new InMemoryTransportModule(), outbox => outbox.UseInMemoryDispatch());
        data.Add(new AmqpTransportModule(new AmqpConnectionOptions { HostName = "localhost" }), outbox => outbox.UseAmqpDispatch(_ =>
        {
        }));
        data.Add(new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b"
        }), outbox => outbox.UseAzureServiceBusDispatch(_ =>
        {
        }));
        data.Add(new AwsSqsTransportModule(new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }), outbox => outbox.UseAwsSqsDispatch(_ =>
        {
        }));
        data.Add(new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" }), outbox => outbox.UseKafkaDispatch(_ =>
        {
        }));
        return data;
    }

    /// <summary>
    ///     Gets inbox ingress configuration cases for each broker package.
    /// </summary>
    /// <returns>The inbox ingress configuration data.</returns>
    public static TheoryData<IModule, Action<InboxModuleBuilder>> InboxIngressConfigurations()
    {
        var data = new TheoryData<IModule, Action<InboxModuleBuilder>>();
        data.Add(new InMemoryTransportModule(), inbox => inbox.UseInMemoryIngress(ingress =>
        {
            ingress.DisableIngressConsumer();
            ingress.UseOptions(new InMemoryInboxIngressOptions { Destination = "commands" });
        }));
        data.Add(new AmqpTransportModule(new AmqpConnectionOptions { HostName = "localhost" }), inbox => inbox.UseAmqpIngress(ingress =>
        {
            ingress.DisableIngressConsumer();
            ingress.UseOptions(new AmqpInboxIngressOptions
            {
                QueueName = "commands"
            });
        }));
        data.Add(new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
        {
            ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b"
        }), inbox => inbox.UseAzureServiceBusIngress(ingress =>
        {
            ingress.DisableIngressConsumer();
            ingress.UseOptions(new AzureServiceBusInboxIngressOptions
            {
                Destination = "commands"
            });
        }));
        data.Add(new AwsSqsTransportModule(new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }), inbox => inbox.UseAwsSqsIngress(ingress =>
        {
            ingress.DisableIngressConsumer();
            ingress.UseOptions(new AwsSqsInboxIngressOptions
            {
                Destination = "http://localhost:4566/000000000000/commands"
            });
        }));
        data.Add(new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" }), inbox => inbox.UseKafkaIngress(ingress =>
        {
            ingress.DisableIngressConsumer();
            ingress.UseOptions(new KafkaInboxIngressOptions
            {
                Destination = "commands"
            });
        }));
        return data;
    }

    /// <summary>
    ///     Gets broker ingress configurations with explicit native and provider-neutral consumer settings.
    /// </summary>
    /// <returns>The broker ingress option mapping cases.</returns>
    public static TheoryData<IModule, Action<InboxModuleBuilder>, Action<TransportInboxIngressOptions>>
        InboxIngressOptionConfigurations()
    {
        var data = new TheoryData<IModule, Action<InboxModuleBuilder>, Action<TransportInboxIngressOptions>>();

        var inMemorySafety = CreateSafetyOptions();
        data.Add(
            new InMemoryTransportModule(),
            inbox => inbox.UseInMemoryIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new InMemoryInboxIngressOptions
                {
                    Destination = "commands",
                    Safety = inMemorySafety
                });
            }),
            options =>
            {
                options.Safety.Should().BeSameAs(inMemorySafety);
                options.PrefetchCount.Should().Be(0);
                options.ReceiveBatchSize.Should().Be(1);
                options.MaxConcurrentCalls.Should().BeNull();
            });

        var amqpSafety = CreateSafetyOptions();
        data.Add(
            new AmqpTransportModule(new AmqpConnectionOptions { HostName = "localhost" }),
            inbox => inbox.UseAmqpIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new AmqpInboxIngressOptions
                {
                    QueueName = "commands",
                    PrefetchCount = 12,
                    Safety = amqpSafety
                });
            }),
            options =>
            {
                options.Safety.Should().BeSameAs(amqpSafety);
                options.PrefetchCount.Should().Be(12);
                options.ReceiveBatchSize.Should().Be(1);
                options.MaxConcurrentCalls.Should().BeNull();
            });

        var azureSafety = CreateSafetyOptions();
        data.Add(
            new AzureServiceBusTransportModule(new AzureServiceBusTransportOptions
            {
                ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b"
            }),
            inbox => inbox.UseAzureServiceBusIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new AzureServiceBusInboxIngressOptions
                {
                    Destination = "commands",
                    PrefetchCount = 13,
                    MaxConcurrentCalls = 4,
                    Safety = azureSafety
                });
            }),
            options =>
            {
                options.Safety.Should().BeSameAs(azureSafety);
                options.PrefetchCount.Should().Be(13);
                options.ReceiveBatchSize.Should().Be(1);
                options.MaxConcurrentCalls.Should().Be(4);
            });

        var awsSafety = CreateSafetyOptions();
        data.Add(
            new AwsSqsTransportModule(new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }),
            inbox => inbox.UseAwsSqsIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new AwsSqsInboxIngressOptions
                {
                    Destination = "http://localhost:4566/000000000000/commands",
                    ReceiveBatchSize = 9,
                    Safety = awsSafety
                });
            }),
            options =>
            {
                options.Safety.Should().BeSameAs(awsSafety);
                options.PrefetchCount.Should().Be(0);
                options.ReceiveBatchSize.Should().Be(9);
                options.MaxConcurrentCalls.Should().BeNull();
            });

        var kafkaSafety = CreateSafetyOptions();
        data.Add(
            new KafkaTransportModule(new KafkaTransportOptions { BootstrapServers = "localhost:9092" }),
            inbox => inbox.UseKafkaIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new KafkaInboxIngressOptions
                {
                    Destination = "commands",
                    Safety = kafkaSafety
                });
            }),
            options =>
            {
                options.Safety.Should().BeSameAs(kafkaSafety);
                options.PrefetchCount.Should().Be(0);
                options.ReceiveBatchSize.Should().Be(1);
                options.MaxConcurrentCalls.Should().BeNull();
            });

        return data;
    }

    /// <summary>
    ///     Creates non-default provider-neutral safety settings for propagation assertions.
    /// </summary>
    /// <returns>The shared safety settings.</returns>
    private static TransportInboxIngressSafetyOptions CreateSafetyOptions()
    {
        return new TransportInboxIngressSafetyOptions
        {
            MaxMessageBytes = 1024,
            RequireStableIdentity = false,
            TrustApplicationHeaders = true,
            AuthorizeDeliveryAsync = static (_, _) => Task.CompletedTask,
            MaxInFlightMessages = 7,
            EnableBatchAccept = true,
            BatchSize = 3,
            BatchMaxWait = TimeSpan.FromMilliseconds(450)
        };
    }
}
