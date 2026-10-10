using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;
using LiteBus.Testing;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.Ingress.Amqp;

/// <summary>
///     Verifies AMQP ingress consumer registration in the host manifest.
/// </summary>
public sealed class AmqpInboxIngressModuleRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies a disabled ingress consumer leaves the background service out of the manifest.
    /// </summary>
    [Fact]
    public void UseAmqpIngress_WithDisabledIngressConsumer_ShouldOmitConsumerFromManifest()
    {
        using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                var connection = new AmqpConnectionOptions { HostName = "localhost" };
                registry.Modules.Register(new AmqpTransportModule(connection));

                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();

                    inbox.UseAmqpIngress(ingress =>
                    {
                        ingress.DisableIngressConsumer();

                        ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = "litebus.inbox.ingress.rabbit",
                        });
                    });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusHostManifest>().BackgroundServices
            .Should().NotContain(typeof(TransportInboxIngressConsumer));
        provider.GetRequiredService<AmqpInboxIngressHandler>().Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies an enabled ingress consumer is registered as a background service in the manifest.
    /// </summary>
    [Fact]
    public async Task UseAmqpIngress_WithEnabledIngressConsumer_ShouldRegisterConsumerInManifest()
    {
        // AmqpConsumer is async-only disposable, so the provider must be disposed asynchronously.
        await using var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                var connection = new AmqpConnectionOptions { HostName = "localhost" };
                registry.Modules.Register(new AmqpTransportModule(connection));

                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();

                    inbox.UseAmqpIngress(ingress =>
                    {
                        ingress.UseOptions(new AmqpInboxIngressOptions
                        {
                            QueueName = "litebus.inbox.ingress.auto-transport",
                        });
                    });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusHostManifest>().BackgroundServices
            .Should().Contain(typeof(TransportInboxIngressConsumer));
        provider.GetRequiredService<AmqpInboxIngressHandler>().Should().NotBeNull();
        provider.GetRequiredService<IMessageConsumer>().Should().NotBeNull();
    }
}
