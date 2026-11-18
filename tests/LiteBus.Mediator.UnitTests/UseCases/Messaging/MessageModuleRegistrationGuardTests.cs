using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Verifies how repeated <see cref="MessageModule" /> registration composes.
/// </summary>
public sealed class MessageModuleRegistrationGuardTests
{
    /// <summary>
    ///     Verifies that a second <c>AddMessaging</c> call configures the module the first call registered instead of
    ///     failing, so several application modules can add to one composition.
    /// </summary>
    [Fact]
    public void AddMessageModule_WhenCalledTwice_RunsBothConfigurationsOnOneModule()
    {
        var firstRan = 0;
        var secondRan = 0;

        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => firstRan++);
                registry.AddMessaging(_ => secondRan++);
            });
        };

        act.Should().NotThrow();
        firstRan.Should().Be(1);
        secondRan.Should().Be(1);
    }
}
