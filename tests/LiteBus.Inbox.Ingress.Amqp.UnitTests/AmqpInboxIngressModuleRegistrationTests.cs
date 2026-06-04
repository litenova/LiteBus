using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Ingress.Amqp;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Ingress.Amqp.UnitTests;

public sealed class AmqpInboxIngressModuleRegistrationTests : LiteBusTestBase
{
    [Fact]
    public void AddInboxRabbitMqIngress_ShouldRegisterIngressHandler()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddInboxModule();
                modules.AddInboxModule(inbox => inbox.UseInMemoryStorage());
                modules.AddInboxRabbitMqIngress(ingress =>
                {
                    ingress.DisableIngressConsumer();
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = "litebus.inbox.ingress.rabbit",
                        Connection = new LiteBus.Transport.Amqp.AmqpConnectionOptions { HostName = "localhost" }
                    });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<AmqpInboxIngressHandler>().Should().NotBeNull();
    }

    [Fact]
    public void AddInboxLavinMqIngress_ShouldRegisterIngressHandler()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddInboxModule();
                modules.AddInboxModule(inbox => inbox.UseInMemoryStorage());
                modules.AddInboxLavinMqIngress(ingress =>
                {
                    ingress.DisableIngressConsumer();
                    ingress.UseOptions(new AmqpInboxIngressOptions
                    {
                        QueueName = "litebus.inbox.ingress.lavin",
                        Connection = new LiteBus.Transport.Amqp.AmqpConnectionOptions { HostName = "localhost" }
                    });
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<AmqpInboxIngressHandler>().Should().NotBeNull();
    }
}
