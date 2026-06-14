using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Dispatch.InMemory;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.Ingress.Amqp;

/// <summary>
///     Verifies AMQP ingress module registration requirements.
/// </summary>
public sealed class AmqpInboxIngressModuleRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies ingress registers when a transport module supplies
    ///     <see cref="LiteBus.Transport.Abstractions.IMessageConsumer" />.
    /// </summary>
    [Fact]
    public void UseAmqpIngress_WithTransportModule_ShouldRegisterIngressHandler()
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
                    inbox.UseInMemoryDispatch();

                    inbox.UseAmqpIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();

                        ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = "litebus.inbox.ingress.rabbit",
                            Connection = new AmqpConnectionOptions { HostName = "localhost" }
                        });
                    });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<AmqpInboxIngressHandler>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies ingress bootstraps AMQP transport from connection options when no consumer is pre-registered.
    /// </summary>
    [Fact]
    public void UseAmqpIngress_WithConnectionOptions_ShouldBootstrapAmqpTransport()
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

                    inbox.UseAmqpIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();

                        ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = "litebus.inbox.ingress.auto-transport",
                            Connection = new AmqpConnectionOptions { HostName = "localhost" }
                        });
                    });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<AmqpInboxIngressHandler>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }
}