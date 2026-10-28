using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.DeclarationGuards;

/// <summary>
///     Verifies that a guard derived from <see cref="DeclarationGuard{TMessage, TValue}" /> receives the declared value,
///     denies what is undeclared with a stable code unless overridden, and allows what is exempt.
/// </summary>
[Collection("Sequential")]
public sealed class DeclarationGuardTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that a definition-declared value reaches the typed decision.
    /// </summary>
    [Fact]
    public async Task A_declared_value_reaches_the_typed_DecideAsync()
    {
        var recorder = new ClearanceRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeClearanceDesk("vaults.lock"),
            typeof(LockVaultCommand),
            typeof(LockVaultCommandHandler),
            typeof(LockVaultCommandDefinition),
            typeof(RecordingClearanceGuard<LockVaultCommand>));
        var command = new LockVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Observed.Should().ContainSingle().Which.Should().Be(new RequiredClearance("vaults.lock"));
    }

    /// <summary>
    ///     Verifies that an attribute-declared value reaches the typed decision the same way a definition does.
    /// </summary>
    [Fact]
    public async Task An_attribute_declared_value_reaches_the_typed_DecideAsync()
    {
        var recorder = new ClearanceRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeClearanceDesk("vaults.rotate"),
            typeof(RotateVaultSealCommand),
            typeof(RotateVaultSealCommandHandler),
            typeof(RecordingClearanceGuard<RotateVaultSealCommand>));
        var command = new RotateVaultSealCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Observed.Should().ContainSingle().Which.Should().Be(new RequiredClearance("vaults.rotate"));
    }

    /// <summary>
    ///     Verifies that a guard closed over a base type enforces the concrete message's declaration.
    /// </summary>
    [Fact]
    public async Task A_guard_closed_over_a_base_type_reads_the_concrete_message_declaration()
    {
        var recorder = new ClearanceRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeClearanceDesk("vaults.lock"),
            typeof(LockVaultCommand),
            typeof(LockVaultCommandHandler),
            typeof(LockVaultCommandDefinition),
            typeof(GlobalClearanceGuard));
        var command = new LockVaultCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Observed.Should().ContainSingle().Which.Should().Be(new RequiredClearance("vaults.lock"));
    }

    /// <summary>
    ///     Verifies that an undeclared message is denied with the stable code and a reason naming the message and the value.
    /// </summary>
    [Fact]
    public async Task An_undeclared_message_is_denied_with_the_stable_code()
    {
        var provider = BuildProvider(
            new ClearanceRecorder(),
            new FakeClearanceDesk(),
            typeof(DraftVaultNoticeCommand),
            typeof(DraftVaultNoticeCommandHandler),
            typeof(RecordingClearanceGuard<DraftVaultNoticeCommand>));
        var command = new DraftVaultNoticeCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be(DeclarationGuard<DraftVaultNoticeCommand, RequiredClearance>.UndeclaredCode);
        DeclarationGuard<DraftVaultNoticeCommand, RequiredClearance>.UndeclaredCode.Should().Be("declaration.missing");
        thrown.Which.Reason.Should().Contain("DraftVaultNoticeCommand").And.Contain("RequiredClearance");
        command.HandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies that overriding the undeclared decision allows a message declaring nothing.
    /// </summary>
    [Fact]
    public async Task An_undeclared_message_can_be_allowed_by_overriding()
    {
        var recorder = new ClearanceRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeClearanceDesk(),
            typeof(DraftVaultNoticeCommand),
            typeof(DraftVaultNoticeCommandHandler),
            typeof(AllowingUndeclaredClearanceGuard));
        var command = new DraftVaultNoticeCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.UndeclaredMessages.Should().ContainSingle().Which.Should().Be("DraftVaultNoticeCommand");
    }

    /// <summary>
    ///     Verifies that a recorded exemption allows without calling the typed decision.
    /// </summary>
    [Fact]
    public async Task An_exemption_allows_without_calling_the_typed_DecideAsync()
    {
        var recorder = new ClearanceRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeClearanceDesk(),
            typeof(BrowseVaultNoticesCommand),
            typeof(BrowseVaultNoticesCommandHandler),
            typeof(RecordingClearanceGuard<BrowseVaultNoticesCommand>));
        var command = new BrowseVaultNoticesCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        recorder.Observed.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that one open generic guard enforces each command's own declaration.
    /// </summary>
    [Fact]
    public async Task One_open_generic_guard_covers_every_command()
    {
        var recorder = new ClearanceRecorder();
        var provider = BuildProvider(
            recorder,
            new FakeClearanceDesk("vaults.lock"),
            typeof(LockVaultCommand),
            typeof(LockVaultCommandHandler),
            typeof(LockVaultCommandDefinition),
            typeof(RotateVaultSealCommand),
            typeof(RotateVaultSealCommandHandler),
            typeof(RecordingClearanceGuard<>));
        var allowed = new LockVaultCommand();
        var denied = new RotateVaultSealCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(allowed).ConfigureAwait(false);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(denied).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Contain("vaults.rotate");

        allowed.HandlerRan.Should().BeTrue();
        denied.HandlerRan.Should().BeFalse();
        recorder.Observed.Select(clearance => clearance.Level).Should().Equal("vaults.lock", "vaults.rotate");
    }

    /// <summary>
    ///     Builds a provider registering the given types with the clearance recorder and desk.
    /// </summary>
    /// <param name="recorder">The recorder shared with the guards under test.</param>
    /// <param name="desk">The clearance desk shared with the guards under test.</param>
    /// <param name="types">The command, handler, definition, and guard types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(ClearanceRecorder recorder, FakeClearanceDesk desk, params Type[] types)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddSingleton(desk);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

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
}
