using System.Security.Claims;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Identity;

/// <summary>
///     Verifies the system execution identity declares library-started work with no principal and no tenant.
/// </summary>
[Collection("Sequential")]
public sealed class SystemIdentityTests : LiteBusTestBase
{
    [Fact]
    public void System_has_no_principal_or_tenant_and_is_system()
    {
        var system = ExecutionIdentity.System;

        system.Principal.Should().BeNull();
        system.TenantId.Should().BeNull();
        system.HasTenant.Should().BeFalse();
        system.IsSystem.Should().BeTrue();
    }

    [Fact]
    public void Anonymous_is_not_system()
    {
        ExecutionIdentity.Anonymous.IsSystem.Should().BeFalse();
    }

    [Fact]
    public void System_differs_from_anonymous()
    {
        ExecutionIdentity.System.Should().NotBe(ExecutionIdentity.Anonymous);
    }

    [Fact]
    public void ForTenant_is_not_system()
    {
        ExecutionIdentity.ForTenant("tenant-a").IsSystem.Should().BeFalse();
        ExecutionIdentity.ForTenant(null).IsSystem.Should().BeFalse();
    }

    [Fact]
    public void For_is_not_system()
    {
        List<Claim> claims = [new(ClaimTypes.NameIdentifier, "user-1")];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        ExecutionIdentity.For(principal, "tenant-a").IsSystem.Should().BeFalse();
        ExecutionIdentity.For(null).IsSystem.Should().BeFalse();
    }

    [Fact]
    public async Task A_system_identity_reaches_the_command_handler()
    {
        var recorder = new IdentityRecorder();
        var provider = BuildProvider(recorder);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InnerIdentityCommand(), new CommandMediationSettings { Identity = ExecutionIdentity.System })
            .ConfigureAwait(false);

        var observed = recorder.ForStage("inner").Should().ContainSingle().Subject;
        observed.Should().Be(ExecutionIdentity.System);
        observed.IsSystem.Should().BeTrue();
    }

    /// <summary>
    ///     Builds a provider registering the inner identity command and its handler.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(IdentityRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<InnerIdentityCommand>();
                    builder.Register<InnerIdentityCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
