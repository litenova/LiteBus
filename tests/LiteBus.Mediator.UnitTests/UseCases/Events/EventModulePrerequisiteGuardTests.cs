using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

/// <summary>
///     Verifies graph prerequisites for <see cref="EventModule" /> registration.
/// </summary>
public sealed class EventModulePrerequisiteGuardTests
{
    /// <summary>
    ///     Verifies that the completed graph requires <see cref="Messaging.MessageModule" />.
    /// </summary>
    [Fact]
    public void AddEventModule_WithoutMessageModule_ShouldFailModuleGraphValidation()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddEvents(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*requires 'MessageModule'*");
    }

    /// <summary>
    ///     Verifies that event and messaging declaration order does not affect the completed graph.
    /// </summary>
    [Fact]
    public void AddEventModule_BeforeMessageModule_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddEvents(_ =>
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
