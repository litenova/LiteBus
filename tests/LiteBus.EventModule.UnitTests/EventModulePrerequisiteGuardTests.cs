using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.EventModule.UnitTests;

/// <summary>
///     Verifies configure-time prerequisites for <see cref="EventModule" /> registration.
/// </summary>
public sealed class EventModulePrerequisiteGuardTests
{
    /// <summary>
    ///     Verifies that <see cref="ModuleRegistryExtensions.AddEventModule" /> requires <see cref="Messaging.MessageModule" />.
    /// </summary>
    [Fact]
    public void AddEventModule_WithoutMessageModule_ShouldThrowLiteBusConfigurationException()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddEventModule(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*AddMessageModule()*");
    }
}
