using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Tenancy;

/// <summary>
///     Verifies that the tenancy guard denies a tenant-required message mediated without a tenant, allows the other
///     two positions, and allows a message declaring no position.
/// </summary>
[Collection("Sequential")]
public sealed class TenancyGuardTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering the tenancy guard and the given types.
    /// </summary>
    /// <param name="types">The command, handler, and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(params Type[] types)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.UseTenancy());

                registry.AddCommands(builder =>
                {
                    foreach (var type in types)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Verifies that a tenant-required message mediated without a tenant is denied with the stable code.
    /// </summary>
    [Fact]
    public async Task A_tenant_required_command_without_a_tenant_is_denied_with_the_stable_code()
    {
        var provider = BuildProvider(typeof(PostWorkspaceEntryCommand), typeof(PostWorkspaceEntryCommandHandler));
        var command = new PostWorkspaceEntryCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(TenancyRefusalCodes.TenantRequired);
        TenancyRefusalCodes.TenantRequired.Should().Be("tenancy.required");
        thrown.Which.Reason.Should().Contain("PostWorkspaceEntryCommand").And.Contain("tenant");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a tenant-required message is allowed when the mediation runs inside a tenant.
    /// </summary>
    [Fact]
    public async Task A_tenant_required_command_with_a_tenant_is_allowed()
    {
        var provider = BuildProvider(typeof(PostWorkspaceEntryCommand), typeof(PostWorkspaceEntryCommandHandler));
        var accessor = provider.GetRequiredService<IMessageMetadataAccessor>();
        var guard = new TenancyGuard<PostWorkspaceEntryCommand>(accessor, provider);
        var command = new PostWorkspaceEntryCommand();

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("workspace-1"))))
        {
            var verdict = await guard.DecideAsync(command).ConfigureAwait(false);

            verdict.IsDenied.Should().BeFalse();
        }
    }

    /// <summary>
    ///     Verifies that a cross-tenant message is allowed without a tenant.
    /// </summary>
    [Fact]
    public async Task A_cross_tenant_command_without_a_tenant_is_allowed()
    {
        var provider = BuildProvider(typeof(PurgeExpiredEntriesCommand), typeof(PurgeExpiredEntriesCommandHandler));
        var command = new PurgeExpiredEntriesCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a tenantless message is allowed without a tenant.
    /// </summary>
    [Fact]
    public async Task A_no_tenant_command_without_a_tenant_is_allowed()
    {
        var provider = BuildProvider(typeof(SignInCommand), typeof(SignInCommandHandler));
        var command = new SignInCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a message declaring no tenancy position is allowed.
    /// </summary>
    [Fact]
    public async Task An_undeclared_command_is_allowed()
    {
        var provider = BuildProvider(typeof(DraftWorkspaceNoticeCommand), typeof(DraftWorkspaceNoticeCommandHandler));
        var command = new DraftWorkspaceNoticeCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a definition-declared tenant requirement denies the same way an attribute does.
    /// </summary>
    [Fact]
    public async Task A_definition_declared_required_command_without_a_tenant_is_denied()
    {
        var provider = BuildProvider(
            typeof(RotateWorkspaceKeyCommand),
            typeof(RotateWorkspaceKeyCommandHandler),
            typeof(RotateWorkspaceKeyCommandTenancyDefinition));
        var command = new RotateWorkspaceKeyCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(TenancyRefusalCodes.TenantRequired);
        thrown.Which.Reason.Should().Contain("RotateWorkspaceKeyCommand");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that one open generic guard enforces each command's own tenancy position.
    /// </summary>
    [Fact]
    public async Task One_open_generic_guard_covers_every_command()
    {
        var provider = BuildProvider(
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler),
            typeof(PurgeExpiredEntriesCommand),
            typeof(PurgeExpiredEntriesCommandHandler),
            typeof(SignInCommand),
            typeof(SignInCommandHandler),
            typeof(DraftWorkspaceNoticeCommand),
            typeof(DraftWorkspaceNoticeCommandHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();
        var denied = new PostWorkspaceEntryCommand();
        var crossTenant = new PurgeExpiredEntriesCommand();
        var noTenant = new SignInCommand();
        var undeclared = new DraftWorkspaceNoticeCommand();

        await mediator.SendAsync(crossTenant).ConfigureAwait(false);
        await mediator.SendAsync(noTenant).ConfigureAwait(false);
        await mediator.SendAsync(undeclared).ConfigureAwait(false);

        var act = async () => await mediator.SendAsync(denied).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(TenancyRefusalCodes.TenantRequired);

        denied.HandlerRan.Should().BeFalse();
        crossTenant.HandlerRan.Should().BeTrue();
        noTenant.HandlerRan.Should().BeTrue();
        undeclared.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that composition fails for a message stating no tenancy position when the declaration is required.
    /// </summary>
    [Fact]
    public void An_undeclared_command_fails_composition_when_the_declaration_is_required()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .UseTenancy()
                    .RequireDeclaration<TenancyDeclaration>());

                registry.AddCommands(builder =>
                {
                    builder.Register<DraftWorkspaceNoticeCommand>();
                    builder.Register<DraftWorkspaceNoticeCommandHandler>();
                });
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*TenancyDeclaration is required of every registered message but is not declared by: DraftWorkspaceNoticeCommand*");
    }
}
