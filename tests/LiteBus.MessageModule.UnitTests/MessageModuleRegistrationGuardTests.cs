using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.MessageModule.UnitTests;

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
                registry.AddMessageModule(_ =>
                {
                });
                registry.AddMessageModule(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*MessageModule is already registered*");
    }
}
