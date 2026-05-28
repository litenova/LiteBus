using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

[Collection("Sequential")]
public sealed class MessageMediatorTests : LiteBusTestBase
{
    [Fact]
    public void Mediate_WhenDescriptorCannotBeResolvedAfterOnTheSpotRegistration_ShouldThrowMessageDescriptorNotFoundException()
    {
        var registry = new MessageRegistry();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var mediator = new MessageMediator(registry, serviceProvider);

        var options = new MediateOptions<string, string>
        {
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            MessageMediationStrategy = new NeverRunMediationStrategy<string, string>(),
            CancellationToken = CancellationToken.None,
            Tags = [],
            RegisterPlainMessagesOnSpot = true
        };

        var act = () => mediator.Mediate("system message", options);

        var exception = act.Should().Throw<MessageDescriptorNotFoundException>();
        exception.Which.MessageType.Should().Be(typeof(string));
        exception.Which.ResolveStrategyType.Should().Be(typeof(ActualTypeOrFirstAssignableTypeMessageResolveStrategy));
        exception.Which.RegisterPlainMessagesOnSpot.Should().BeTrue();
        exception.Which.RegisteredMessageCount.Should().Be(0);
    }

    private sealed class NeverRunMediationStrategy<TMessage, TMessageResult> : IMessageMediationStrategy<TMessage, TMessageResult>
        where TMessage : notnull
    {
        public TMessageResult Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
        {
            throw new InvalidOperationException("The mediation strategy should not run when descriptor resolution fails.");
        }
    }
}