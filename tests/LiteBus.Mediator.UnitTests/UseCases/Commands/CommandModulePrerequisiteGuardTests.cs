using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Verifies configure-time prerequisites for <see cref="CommandModule" /> registration.
/// </summary>
public sealed class CommandModulePrerequisiteGuardTests
{
    /// <summary>
    ///     Verifies that <see cref="ModuleRegistryExtensions.AddCommandModule" /> requires <see cref="Messaging.MessageModule" />.
    /// </summary>
    [Fact]
    public void AddCommandModule_WithoutMessageModule_ShouldThrowLiteBusConfigurationException()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddCommandModule(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*AddMessageModule()*");
    }
}
