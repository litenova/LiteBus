using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Dependencies;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

[Collection("Sequential")]
public sealed class MessageMediatorGuardTests : LiteBusTestBase
{
    [Fact]
    public void Mediate_NullMessage_ThrowsArgumentNullException()
    {
        var mediator = BuildMediator();
        var request = BuildRequest();

        var act = () => mediator.Mediate<string, string>(null!, request);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("message");
    }

    [Fact]
    public void Mediate_NullRequest_ThrowsArgumentNullException()
    {
        var mediator = BuildMediator();

        var act = () => mediator.Mediate<string, string>("message", null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("request");
    }

    private static MessageMediator BuildMediator()
    {
        var registry = new MessageRegistry();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        return new MessageMediator(registry, registry, new RootMessageDispatchScopeFactory(serviceProvider));
    }

    private static MessageMediationRequest<string, string> BuildRequest()
    {
        return new MessageMediationRequest<string, string>
        {
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            MessageMediationStrategy = new NeverRunMediationStrategy(),
            Tags = [],
            RegisterPlainMessagesOnSpot = true
        };
    }

    private sealed class NeverRunMediationStrategy : IMessageMediationStrategy<string, string>
    {
        public string Mediate(string message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
        {
            throw new InvalidOperationException("The mediation strategy should not run when the arguments are invalid.");
        }
    }
}
