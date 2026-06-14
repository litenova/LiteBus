using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Queries;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries;

/// <summary>
///     Verifies configure-time prerequisites for <see cref="QueryModule" /> registration.
/// </summary>
public sealed class QueryModulePrerequisiteGuardTests
{
    /// <summary>
    ///     Verifies that <see cref="ModuleRegistryExtensions.AddQueryModule" /> requires <see cref="Messaging.MessageModule" />.
    /// </summary>
    [Fact]
    public void AddQueryModule_WithoutMessageModule_ShouldThrowLiteBusConfigurationException()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddQueryModule(_ =>
                {
                });
            });
        };

        act.Should()
            .Throw<LiteBusConfigurationException>()
            .WithMessage("*AddMessageModule()*");
    }
}
