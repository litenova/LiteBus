using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

public sealed class CommandModuleBuilderTests : LiteBusTestBase
{
    [Fact]
    public void RegisterFromAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        var act = () =>
        {
            new ServiceCollection().AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(module => module.RegisterFromAssembly(null!));
            });
        };

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterFromAssembly_DoesNotRegisterMarkerInterfaces()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(module => module.RegisterFromAssembly(typeof(ICommand).Assembly));
            })
            .BuildServiceProvider();

        var registry = serviceProvider.GetRequiredService<IMessageRegistry>();

        registry
            .Should()
            .NotContain(descriptor => descriptor.MessageType == typeof(ICommand));
    }
}
