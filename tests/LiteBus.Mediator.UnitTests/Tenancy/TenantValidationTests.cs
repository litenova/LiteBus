using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Tenancy;

/// <summary>
///     Verifies that tenant validation configured through <see cref="TenancyOptions" /> denies a malformed tenant
///     with the invalid code, and that validator configuration follows the registration conflict rule.
/// </summary>
[Collection("Sequential")]
public sealed class TenantValidationTests : LiteBusTestBase
{
    /// <summary>
    ///     Accepts workspace identifiers shaped as GUIDs. Shared so the identical-repeat test configures one instance.
    /// </summary>
    private static readonly Func<string, bool> WorkspaceIdIsValid = static tenantId => Guid.TryParse(tenantId, out _);

    /// <summary>
    ///     Builds a provider registering tenancy with the given validation and the given types.
    /// </summary>
    /// <param name="configureTenancy">Configures tenant validation, or <see langword="null" /> for the default.</param>
    /// <param name="types">The command, handler, and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(Action<TenancyOptions>? configureTenancy, params Type[] types)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.UseTenancy(configureTenancy));

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
    ///     Verifies that a tenant-required message running in a malformed tenant is denied with the invalid code.
    /// </summary>
    [Fact]
    public async Task A_tenant_required_command_with_a_malformed_tenant_is_denied_with_the_invalid_code()
    {
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid),
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler));
        var command = new PostWorkspaceEntryCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command, CommandMediationSettings.ForTenant("not-a-workspace-id")).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(TenancyRefusalCodes.Invalid);
        TenancyRefusalCodes.Invalid.Should().Be("tenancy.invalid");
        thrown.Which.Reason.Should().Contain("PostWorkspaceEntryCommand").And.Contain("not-a-workspace-id");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that a tenant-required message running in a well-formed tenant is allowed.
    /// </summary>
    [Fact]
    public async Task A_tenant_required_command_with_a_valid_tenant_is_allowed()
    {
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid),
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler));
        var command = new PostWorkspaceEntryCommand();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command, CommandMediationSettings.ForTenant(Guid.NewGuid().ToString())).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that without a validator any non-blank tenant is allowed.
    /// </summary>
    [Fact]
    public async Task Without_a_validator_any_non_blank_tenant_is_allowed()
    {
        var provider = BuildProvider(
            null,
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler));
        var command = new PostWorkspaceEntryCommand();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command, CommandMediationSettings.ForTenant("not-a-workspace-id")).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a cross-tenant message is allowed when the ambient tenant is malformed, because the message
    ///     runs outside one tenant.
    /// </summary>
    [Fact]
    public async Task A_cross_tenant_command_with_a_malformed_tenant_is_allowed()
    {
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid),
            typeof(PurgeExpiredEntriesCommand),
            typeof(PurgeExpiredEntriesCommandHandler));
        var command = new PurgeExpiredEntriesCommand();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command, CommandMediationSettings.ForTenant("not-a-workspace-id")).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a tenantless message is allowed when the ambient tenant is malformed, because the message
    ///     runs outside one tenant.
    /// </summary>
    [Fact]
    public async Task A_no_tenant_command_with_a_malformed_tenant_is_allowed()
    {
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid),
            typeof(SignInCommand),
            typeof(SignInCommandHandler));
        var command = new SignInCommand();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command, CommandMediationSettings.ForTenant("not-a-workspace-id")).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    /// <summary>
    ///     Verifies that a missing tenant is still denied with the required code when a validator is configured.
    /// </summary>
    [Fact]
    public async Task A_missing_tenant_is_still_denied_with_the_required_code()
    {
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid),
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler));
        var command = new PostWorkspaceEntryCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(TenancyRefusalCodes.TenantRequired);
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that two composition helpers configuring the same validator compose without failing.
    /// </summary>
    [Fact]
    public void An_identical_validator_repeats_without_failing()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .UseTenancy(tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid)));
                registry.AddMessaging(messaging => messaging
                    .UseTenancy(tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid)));

                registry.AddCommands(builder =>
                {
                    builder.Register<PostWorkspaceEntryCommand>();
                    builder.Register<PostWorkspaceEntryCommandHandler>();
                });
            })
            .BuildServiceProvider();

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies that configuring a different validator fails composition naming both.
    /// </summary>
    [Fact]
    public void A_conflicting_validator_fails_composition_naming_both()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging
                    .UseTenancy(tenancy => tenancy.ValidateTenant(WorkspaceIdIsValid)));
                registry.AddMessaging(messaging => messaging
                    .UseTenancy(tenancy => tenancy.ValidateTenant(tenantId => tenantId.Length > 3)));

                registry.AddCommands(builder =>
                {
                    builder.Register<PostWorkspaceEntryCommand>();
                    builder.Register<PostWorkspaceEntryCommandHandler>();
                });
            })
            .BuildServiceProvider();

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*already configured from a validation predicate*a validation predicate conflicts with it*");
    }

    /// <summary>
    ///     Verifies that a validator type is constructed from the container and denies a malformed tenant.
    /// </summary>
    [Fact]
    public async Task A_validator_type_is_constructed_from_the_container()
    {
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenant<WorkspaceGuidValidator>(),
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler));
        var denied = new PostWorkspaceEntryCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(denied, CommandMediationSettings.ForTenant("not-a-workspace-id")).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(TenancyRefusalCodes.Invalid);
        denied.HandlerRan.Should().BeFalse();

        var allowed = new PostWorkspaceEntryCommand();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(allowed, CommandMediationSettings.ForTenant(Guid.NewGuid().ToString())).ConfigureAwait(false);

        allowed.HandlerRan.Should().BeTrue();
        provider.GetRequiredService<ITenantIdentifierValidator>().Should().BeOfType<WorkspaceGuidValidator>()
            .Which.Clock.Should().BeSameAs(TimeProvider.System);
    }

    /// <summary>
    ///     Verifies that a validator instance observes the tenant and the dispatch scope.
    /// </summary>
    [Fact]
    public async Task A_validator_instance_observes_the_tenant_and_the_services()
    {
        var validator = new RecordingTenantValidator();
        var provider = BuildProvider(
            tenancy => tenancy.ValidateTenantInstance(validator),
            typeof(PostWorkspaceEntryCommand),
            typeof(PostWorkspaceEntryCommandHandler));
        var command = new PostWorkspaceEntryCommand();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command, CommandMediationSettings.ForTenant("workspace-7")).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        validator.SeenTenants.Should().ContainSingle().Which.Should().Be("workspace-7");
        validator.ServicesObserved.Should().BeTrue();
        provider.GetRequiredService<ITenantIdentifierValidator>().Should().BeSameAs(validator);
    }
}

/// <summary>
///     Validates workspace identifiers as GUIDs, constructed from the container with the configured clock.
/// </summary>
internal sealed class WorkspaceGuidValidator : ITenantIdentifierValidator
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="WorkspaceGuidValidator" /> class.
    /// </summary>
    /// <param name="clock">The configured time provider, proving container construction.</param>
    public WorkspaceGuidValidator(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Clock = clock;
    }

    /// <summary>
    ///     Gets the configured time provider.
    /// </summary>
    internal TimeProvider Clock { get; }

    /// <inheritdoc />
    public ValueTask<bool> IsValidAsync(
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(Guid.TryParse(tenantId, out _));
    }
}

/// <summary>
///     Accepts every tenant while recording what it observed.
/// </summary>
internal sealed class RecordingTenantValidator : ITenantIdentifierValidator
{
    /// <summary>
    ///     The tenant identifiers observed, in the order they were validated.
    /// </summary>
    private readonly List<string> _seenTenants = [];

    /// <summary>
    ///     Gets the tenant identifiers observed, in the order they were validated.
    /// </summary>
    internal IReadOnlyList<string> SeenTenants => _seenTenants;

    /// <summary>
    ///     Gets a value indicating whether the validator observed a service provider.
    /// </summary>
    internal bool ServicesObserved { get; private set; }

    /// <inheritdoc />
    public ValueTask<bool> IsValidAsync(
        string tenantId,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        _seenTenants.Add(tenantId);
        ServicesObserved = services is not null;

        return new ValueTask<bool>(true);
    }
}
