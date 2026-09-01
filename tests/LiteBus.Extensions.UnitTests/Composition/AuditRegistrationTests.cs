using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Extensions.UnitTests.Composition;

/// <summary>
///     Covers how the audit services are registered, which is the part of auditing every application pays for whether or
///     not it audits anything.
/// </summary>
public sealed class AuditRegistrationTests
{
    [Fact]
    public void A_container_that_audits_nothing_still_validates_on_build()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>());
        });

        // The record writer needs an IAuditTrail that only an auditing application registers. Registering the writer by
        // type made ValidateOnBuild construct that dependency graph at startup, so an application that never enabled
        // auditing could not build its container at all.
        var build = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        build.Should().NotThrow();
    }

    [Fact]
    public void Resolving_the_writer_without_a_trail_names_the_fix()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>().EnableAuditing());
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Deferring the trail lookup to resolution time must not turn a missing registration into a null reference on
        // the completion path, where faults are deliberately kept away from the caller.
        var resolve = () => scope.ServiceProvider.GetRequiredService<IAuditRecordWriter>();

        resolve.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*UseAuditTrail*");
    }

    [Fact]
    public void A_trail_registered_on_the_builder_reaches_the_writer()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(messaging => messaging.UseAuditTrail<AuditProbeTrail>());
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>().EnableAuditing());
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAuditTrail>().Should().BeOfType<AuditProbeTrail>();
        scope.ServiceProvider.GetRequiredService<IAuditRecordWriter>().Should().NotBeNull();
    }

    [Fact]
    public void A_trail_registered_with_the_application_container_still_reaches_the_writer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuditTrail, AuditProbeTrail>();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>().EnableAuditing());
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // The builder overload is the documented route, but registering the trail directly has to keep working: it is
        // what every application on the first release of auditing does.
        scope.ServiceProvider.GetRequiredService<IAuditRecordWriter>().Should().NotBeNull();
    }

    [Fact]
    public void The_outcome_mapper_defaults_when_the_application_registers_none()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>());
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAuditOutcomeMapper>().Should().NotBeNull();
    }

    /// <summary>
    ///     A command that exists only to give the command module something to register.
    /// </summary>
    private sealed record AuditProbeCommand : ICommand;

    /// <summary>
    ///     A trail that records nothing, for asserting on registration rather than on writing.
    /// </summary>
    private sealed class AuditProbeTrail : IAuditTrail
    {
        /// <inheritdoc />
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
