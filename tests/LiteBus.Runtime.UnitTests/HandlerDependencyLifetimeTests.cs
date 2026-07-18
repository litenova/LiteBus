using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests;

public sealed class HandlerDependencyLifetimeTests
{
    [Fact]
    public void AddLiteBus_WhenMessageAndCommandModulesRegisterSameHandler_ShouldRegisterHandlerAsScoped()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(message => message.Register<ScopedLifetimeProbeHandler>());
            registry.AddCommands(command => command.Register<ScopedLifetimeProbeCommand>());
        });

        var handlerDescriptor = services.Single(descriptor =>
            descriptor.ServiceType == typeof(ScopedLifetimeProbeHandler));

        handlerDescriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    /// <summary>
    ///     Probe command used only to assert handler dependency injection lifetime registration.
    /// </summary>
    private sealed record ScopedLifetimeProbeCommand : ICommand;

    /// <summary>
    ///     Handler registered when both message and command modules participate in composition.
    /// </summary>
    private sealed class ScopedLifetimeProbeHandler : ICommandHandler<ScopedLifetimeProbeCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(ScopedLifetimeProbeCommand command, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
