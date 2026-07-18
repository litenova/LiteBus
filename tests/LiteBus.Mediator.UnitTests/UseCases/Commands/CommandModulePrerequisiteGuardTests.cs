using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Verifies graph prerequisites for <see cref="CommandModule" /> registration.
/// </summary>
public sealed class CommandModulePrerequisiteGuardTests
{
    /// <summary>
    ///     Verifies that the completed graph requires <see cref="Messaging.MessageModule" />.
    /// </summary>
    [Fact]
    public void AddCommandModule_WithoutMessageModule_ShouldFailModuleGraphValidation()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddCommands(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*requires 'MessageModule'*");
    }

    /// <summary>
    ///     Verifies that command and messaging declaration order does not affect the completed graph.
    /// </summary>
    [Fact]
    public void AddCommandModule_BeforeMessageModule_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddCommands(_ =>
                {
                });
                registry.AddMessaging(_ =>
                {
                });
            });
        };

        act.Should().NotThrow();
    }
}
