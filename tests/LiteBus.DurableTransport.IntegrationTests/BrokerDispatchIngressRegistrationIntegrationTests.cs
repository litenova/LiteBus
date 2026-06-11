using LiteBus.DurableTransport.IntegrationTesting;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch;
using LiteBus.Inbox.Dispatch.Amqp;
using LiteBus.Inbox.Dispatch.Aws;
using LiteBus.Inbox.Dispatch.AzureServiceBus;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Dispatch.Kafka;
using LiteBus.Inbox.Ingress;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Ingress.Aws;
using LiteBus.Inbox.Ingress.AzureServiceBus;
using LiteBus.Inbox.Ingress.InMemory;
using LiteBus.Inbox.Ingress.Kafka;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch;
using LiteBus.Outbox.Dispatch.Amqp;
using LiteBus.Outbox.Dispatch.Aws;
using LiteBus.Outbox.Dispatch.AzureServiceBus;
using LiteBus.Outbox.Dispatch.InMemory;
using LiteBus.Outbox.Dispatch.Kafka;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using LiteBus.Transport.Aws;
using LiteBus.Transport.AzureServiceBus;
using LiteBus.Transport.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests;

/// <summary>
///     Verifies every broker dispatch and ingress extension resolves transport services without a live broker.
/// </summary>
[Trait("Category", TransportTestTraits.Fast)]
public sealed class BrokerDispatchIngressRegistrationIntegrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies inbox broker dispatch extensions register <see cref="TransportInboxDispatcher" /> and transport services.
    /// </summary>
    /// <param name="configure">The inbox module configuration action.</param>
    [Theory]
    [MemberData(nameof(InboxDispatchConfigurations))]
    public void InboxDispatchExtensions_ShouldRegisterTransportDispatcher(Action<InboxModuleBuilder> configure)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    configure(inbox);
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IInboxDispatcher>().Should().BeOfType<TransportInboxDispatcher>();
        provider.GetRequiredService<IMessageTransport>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies outbox broker dispatch extensions register <see cref="TransportOutboxDispatcher" /> and transport
    ///     services.
    /// </summary>
    /// <param name="configure">The outbox module configuration action.</param>
    [Theory]
    [MemberData(nameof(OutboxDispatchConfigurations))]
    public void OutboxDispatchExtensions_ShouldRegisterTransportDispatcher(Action<OutboxModuleBuilder> configure)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    configure(outbox);
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<IOutboxDispatcher>().Should().BeOfType<TransportOutboxDispatcher>();
        provider.GetRequiredService<IMessageTransport>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies broker ingress extensions register the shared ingress handler and consumer types.
    /// </summary>
    /// <param name="configure">The inbox module configuration action.</param>
    [Theory]
    [MemberData(nameof(InboxIngressConfigurations))]
    public void InboxIngressExtensions_ShouldRegisterIngressServices(Action<InboxModuleBuilder> configure)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddInboxModule(inbox =>
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
    ///     Gets inbox dispatch configuration cases for each broker package.
    /// </summary>
    /// <returns>The inbox dispatch configuration data.</returns>
    public static TheoryData<Action<InboxModuleBuilder>> InboxDispatchConfigurations()
    {
        return
        [
            inbox => inbox.UseInMemoryDispatch(),
            inbox => inbox.UseAmqpDispatch(_ =>
            {
            }, new AmqpConnectionOptions { HostName = "localhost" }),
            inbox => inbox.UseAzureServiceBusDispatch(
                _ =>
                {
                },
                new AzureServiceBusTransportOptions { ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b" }),
            inbox => inbox.UseAwsSqsDispatch(_ =>
            {
            }, new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }),
            inbox => inbox.UseKafkaDispatch(_ =>
            {
            }, new KafkaTransportOptions { BootstrapServers = "localhost:9092" })
        ];
    }

    /// <summary>
    ///     Gets outbox dispatch configuration cases for each broker package.
    /// </summary>
    /// <returns>The outbox dispatch configuration data.</returns>
    public static TheoryData<Action<OutboxModuleBuilder>> OutboxDispatchConfigurations()
    {
        return
        [
            outbox => outbox.UseInMemoryDispatch(),
            outbox => outbox.UseAmqpDispatch(_ =>
            {
            }, new AmqpConnectionOptions { HostName = "localhost" }),
            outbox => outbox.UseAzureServiceBusDispatch(
                _ =>
                {
                },
                new AzureServiceBusTransportOptions { ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b" }),
            outbox => outbox.UseAwsSqsDispatch(_ =>
            {
            }, new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }),
            outbox => outbox.UseKafkaDispatch(_ =>
            {
            }, new KafkaTransportOptions { BootstrapServers = "localhost:9092" })
        ];
    }

    /// <summary>
    ///     Gets inbox ingress configuration cases for each broker package.
    /// </summary>
    /// <returns>The inbox ingress configuration data.</returns>
    public static TheoryData<Action<InboxModuleBuilder>> InboxIngressConfigurations()
    {
        return
        [
            inbox => inbox.UseInMemoryIngress(ingress =>
            {
                ingress.DisableIngressConsumer();
                ingress.UseOptions(new InMemoryInboxIngressOptions { Destination = "commands" });
            }),
            inbox => inbox.UseAmqpIngress(ingress =>
            {
                ingress.DisableIngressConsumer();

                ingress.UseOptions(new AmqpInboxIngressOptions
                {
                    QueueName = "commands",
                    Connection = new AmqpConnectionOptions { HostName = "localhost" }
                });
            }),
            inbox => inbox.UseAzureServiceBusIngress(ingress =>
            {
                ingress.DisableIngressConsumer();

                ingress.UseOptions(new AzureServiceBusInboxIngressOptions
                {
                    Destination = "commands",
                    Connection = new AzureServiceBusTransportOptions
                    {
                        ConnectionString = "Endpoint=sb://example/;SharedAccessKeyName=a;SharedAccessKey=b"
                    }
                });
            }),
            inbox => inbox.UseAwsSqsIngress(ingress =>
            {
                ingress.DisableIngressConsumer();

                ingress.UseOptions(new AwsSqsInboxIngressOptions
                {
                    Destination = "http://localhost:4566/000000000000/commands",
                    Connection = new AwsSqsTransportOptions { ServiceUrl = "http://localhost:4566" }
                });
            }),
            inbox => inbox.UseKafkaIngress(ingress =>
            {
                ingress.DisableIngressConsumer();

                ingress.UseOptions(new KafkaInboxIngressOptions
                {
                    Destination = "commands",
                    Connection = new KafkaTransportOptions { BootstrapServers = "localhost:9092" }
                });
            })
        ];
    }
}