using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
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
    public void Composing_an_axis_with_no_receiver_names_the_fix()
    {
        var services = new ServiceCollection();

        var compose = () => services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>());
            liteBus.Modules.AddAuditing(auditing => auditing.ForCommands());
        });

        // A module with no store and no application trail cannot write a record, so composition rejects it before the
        // first mediation reaches the completion stage, where faults are deliberately kept away from the caller.
        compose.Should().Throw<AuditConfigurationException>()
            .WithMessage("*UseInMemoryStore*");
    }

    [Fact]
    public void A_trail_registered_on_the_builder_reaches_the_writer()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>());
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseTrail<AuditProbeTrail>());
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
    public void The_outcome_mapper_defaults_when_the_application_registers_none()
    {
        var services = new ServiceCollection();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>());
            liteBus.Modules.AddAuditing(auditing => auditing
                .ForCommands()
                .UseTrail<AuditProbeTrail>());
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAuditOutcomeMapper>().Should().NotBeNull();
    }

    [Fact]
    public void A_trail_type_is_scoped_by_default()
    {
        using var provider = BuildProvider(auditing => auditing.UseTrail<SessionBoundTrail>());

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        // A trail wrapping a database session has to be scoped. It used to be scoped only as a consequence of which
        // overload the caller reached for, with nothing at the call site saying so.
        first.ServiceProvider.GetRequiredService<IAuditTrail>()
            .Should().NotBeSameAs(second.ServiceProvider.GetRequiredService<IAuditTrail>());
    }

    [Fact]
    public void A_trail_type_registered_as_a_singleton_says_so_at_the_call_site()
    {
        using var provider = BuildProvider(auditing =>
            auditing.UseTrail<AuditProbeTrail>(InstanceLifetime.Singleton));

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        first.ServiceProvider.GetRequiredService<IAuditTrail>()
            .Should().BeSameAs(second.ServiceProvider.GetRequiredService<IAuditTrail>());
    }

    [Fact]
    public void A_pre_created_trail_instance_is_a_singleton()
    {
        var instance = new AuditProbeTrail();
        using var provider = BuildProvider(auditing => auditing.UseTrailInstance(instance));

        using var scope = provider.CreateScope();

        // The method name carries the lifetime, because a pre-created instance can only be one.
        scope.ServiceProvider.GetRequiredService<IAuditTrail>().Should().BeSameAs(instance);
    }

    [Fact]
    public async Task The_probe_reports_whether_the_trail_is_a_singleton()
    {
        using var scoped = BuildProvider(auditing => auditing.UseTrail<SessionBoundTrail>());
        using var singleton = BuildProvider(auditing => auditing.UseTrailInstance(new AuditProbeTrail()));

        var scopedResult = await ProbeAsync(scoped).ConfigureAwait(false);
        var singletonResult = await ProbeAsync(singleton).ConfigureAwait(false);

        // A singleton trail holding a scoped session produces no error until the captured session misbehaves under
        // load, so the probe names it while the application is still starting.
        scopedResult.Data!["trailIsSingleton"].Should().Be(false);
        singletonResult.Data!["trailIsSingleton"].Should().Be(true);
    }

    /// <summary>
    ///     Builds a provider with auditing enabled on the command axis and the given auditing configuration.
    /// </summary>
    /// <param name="configureAuditing">The auditing configuration under test.</param>
    /// <returns>The built service provider.</returns>
    private static ServiceProvider BuildProvider(Action<AuditingModuleBuilder> configureAuditing)
    {
        var services = new ServiceCollection();
        services.AddScoped<FakeSession>();

        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ => { });
            liteBus.AddCommands(commands => commands.Register<AuditProbeCommand>());
            liteBus.Modules.AddAuditing(auditing =>
            {
                auditing.ForCommands();
                configureAuditing(auditing);
            });
        });

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    /// <summary>
    ///     Runs the audit trail probe against a built provider.
    /// </summary>
    /// <param name="provider">The provider to probe.</param>
    /// <returns>The probe result.</returns>
    private static async Task<DiagnosticResult> ProbeAsync(ServiceProvider provider)
    {
        return await new AuditTrailDiagnosticCheck(provider).CheckAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     A command that exists only to give the command module something to register.
    /// </summary>
    private sealed record AuditProbeCommand : ICommand;

    /// <summary>
    ///     A scoped dependency standing in for a database session a trail would capture.
    /// </summary>
    private sealed class FakeSession;

    /// <summary>
    ///     A trail taking a scoped dependency, so its own lifetime has to be scoped too.
    /// </summary>
    private sealed class SessionBoundTrail : IAuditTrail
    {
        /// <summary>
        ///     The session this trail would write through.
        /// </summary>
        private readonly FakeSession _session;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SessionBoundTrail" /> class.
        /// </summary>
        /// <param name="session">The session this trail would write through.</param>
        public SessionBoundTrail(FakeSession session)
        {
            _session = session;
        }

        /// <inheritdoc />
        public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
        {
            _ = _session;
            return Task.CompletedTask;
        }
    }

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
