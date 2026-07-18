using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Verifies configure-time guards for <see cref="MessageModule" /> registration.
/// </summary>
public sealed class MessageModuleRegistrationGuardTests
{
    /// <summary>
    ///     Verifies that registering <see cref="MessageModule" /> twice fails at compose time.
    /// </summary>
    [Fact]
    public void AddMessageModule_WhenCalledTwice_ShouldThrowLiteBusConfigurationException()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });
                registry.AddMessaging(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*MessageModule*already registered*");
    }
}
