using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries;

/// <summary>
///     Verifies graph prerequisites for <see cref="QueryModule" /> registration.
/// </summary>
public sealed class QueryModulePrerequisiteGuardTests
{
    /// <summary>
    ///     Verifies that the completed graph requires <see cref="Messaging.MessageModule" />.
    /// </summary>
    [Fact]
    public void AddQueryModule_WithoutMessageModule_ShouldFailModuleGraphValidation()
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
            .WithMessage("*requires 'MessageModule'*");
    }

    /// <summary>
    ///     Verifies that query and messaging declaration order does not affect the completed graph.
    /// </summary>
    [Fact]
    public void AddQueryModule_BeforeMessageModule_ShouldSucceed()
    {
        var act = () =>
        {
            _ = new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddQueryModule(_ =>
                {
                });
                registry.AddMessageModule(_ =>
                {
                });
            });
        };

        act.Should().NotThrow();
    }
}
