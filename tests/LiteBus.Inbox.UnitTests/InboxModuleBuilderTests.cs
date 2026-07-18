using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies inbox module composition constraints.
/// </summary>
public sealed class InboxModuleBuilderTests
{
    /// <summary>
    ///     Verifies that one inbox module cannot own multiple ingress consumers.
    /// </summary>
    [Fact]
    public void RegisterIngress_WhenIngressIsAlreadyConfigured_ShouldThrow()
    {
        var builder = new InboxModuleBuilder();
        builder.RegisterIngress(new TestInboxIngressModule());

        var act = () => builder.RegisterIngress(new TestInboxIngressModule());

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*one ingress source per inbox module*");
    }

    private sealed class TestInboxIngressModule : IInboxIngressModule
    {
        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
