using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.DeclarationGuards;

/// <summary>
///     A clearance level an application declares on a message, standing in for any consumer-defined declaration.
/// </summary>
/// <param name="Level">The clearance level identifier.</param>
internal sealed record RequiredClearance(string Level);

/// <summary>
///     Declares the clearance a message requires as an attribute rather than a definition class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class RequiresClearanceAttribute : Attribute, IMessageDeclarationSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RequiresClearanceAttribute" /> class.
    /// </summary>
    /// <param name="level">The clearance level identifier.</param>
    public RequiresClearanceAttribute(string level)
    {
        Level = level;
    }

    /// <summary>
    ///     Gets the clearance level identifier.
    /// </summary>
    public string Level { get; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(RequiredClearance);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return new RequiredClearance(Level);
    }
}

/// <summary>
///     Records the declarations and undeclared messages the clearance guards observe.
/// </summary>
internal sealed class ClearanceRecorder
{
    /// <summary>
    ///     Gets the declarations passed to the typed decision, in observation order.
    /// </summary>
    public List<RequiredClearance> Observed { get; } = [];

    /// <summary>
    ///     Gets the names of the messages that reached the undeclared decision, in observation order.
    /// </summary>
    public List<string> UndeclaredMessages { get; } = [];
}

/// <summary>
///     Decides which clearance levels the calling actor holds.
/// </summary>
internal sealed class FakeClearanceDesk
{
    /// <summary>
    ///     The clearance levels the actor holds.
    /// </summary>
    private readonly HashSet<string> _held;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeClearanceDesk" /> class.
    /// </summary>
    /// <param name="held">The clearance levels the actor holds.</param>
    public FakeClearanceDesk(params string[] held)
    {
        _held = [.. held];
    }

    /// <summary>
    ///     Determines whether the actor holds the given clearance level.
    /// </summary>
    /// <param name="level">The clearance level to check.</param>
    /// <returns><see langword="true" /> when the actor holds it.</returns>
    public bool Holds(string level)
    {
        ArgumentNullException.ThrowIfNull(level);
        return _held.Contains(level);
    }
}

/// <summary>
///     Records the declaration it receives and enforces it against the clearance desk.
/// </summary>
/// <typeparam name="TCommand">The command type the guard is closed over at registration.</typeparam>
internal class RecordingClearanceGuard<TCommand> : DeclarationGuard<TCommand, RequiredClearance>
    where TCommand : ICommand
{
    /// <summary>
    ///     Decides which clearance levels the calling actor holds.
    /// </summary>
    private readonly FakeClearanceDesk _desk;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingClearanceGuard{TCommand}" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type.</param>
    /// <param name="recorder">Records the declarations the guard observes.</param>
    /// <param name="desk">Decides which clearance levels the calling actor holds.</param>
    public RecordingClearanceGuard(IMessageMetadataAccessor metadata, ClearanceRecorder recorder, FakeClearanceDesk desk)
        : base(metadata)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(desk);

        Recorder = recorder;
        _desk = desk;
    }

    /// <summary>
    ///     Gets the recorder shared with the test.
    /// </summary>
    protected ClearanceRecorder Recorder { get; }

    /// <inheritdoc />
    protected override Task<Verdict> DecideAsync(RequiredClearance declaration, TCommand message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(message);

        Recorder.Observed.Add(declaration);

        return Task.FromResult(_desk.Holds(declaration.Level)
            ? Verdict.Allow
            : Verdict.Deny($"the caller lacks {declaration.Level}"));
    }
}

/// <summary>
///     Allows messages declaring no clearance, proving the undeclared decision can be overridden.
/// </summary>
internal sealed class AllowingUndeclaredClearanceGuard : RecordingClearanceGuard<DraftVaultNoticeCommand>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AllowingUndeclaredClearanceGuard" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type.</param>
    /// <param name="recorder">Records the declarations the guard observes.</param>
    /// <param name="desk">Decides which clearance levels the calling actor holds.</param>
    public AllowingUndeclaredClearanceGuard(IMessageMetadataAccessor metadata, ClearanceRecorder recorder, FakeClearanceDesk desk)
        : base(metadata, recorder, desk)
    {
    }

    /// <inheritdoc />
    protected override Task<Verdict> DecideUndeclaredAsync(DraftVaultNoticeCommand message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        Recorder.UndeclaredMessages.Add(message.GetType().Name);

        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     Enforces the clearance declaration for every command, proving the lookup reads the concrete message type.
/// </summary>
/// <remarks>
///     Closed over <see cref="ICommand" />, which is never registered as a message, so only the runtime-type lookup can
///     find a declaration. A lookup keyed on the guard's own message type would report every message as unregistered.
/// </remarks>
internal sealed class GlobalClearanceGuard : RecordingClearanceGuard<ICommand>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="GlobalClearanceGuard" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type.</param>
    /// <param name="recorder">Records the declarations the guard observes.</param>
    /// <param name="desk">Decides which clearance levels the calling actor holds.</param>
    public GlobalClearanceGuard(IMessageMetadataAccessor metadata, ClearanceRecorder recorder, FakeClearanceDesk desk)
        : base(metadata, recorder, desk)
    {
    }
}

/// <summary>
///     A command whose clearance is declared in a definition class.
/// </summary>
internal sealed class LockVaultCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Declares the clearance <see cref="LockVaultCommand" /> requires.
/// </summary>
internal sealed class LockVaultCommandDefinition : IMessageDefinition<LockVaultCommand, RequiredClearance>
{
    /// <inheritdoc />
    public RequiredClearance Value => new("vaults.lock");
}

/// <summary>
///     Handles <see cref="LockVaultCommand" />.
/// </summary>
internal sealed class LockVaultCommandHandler : ICommandHandler<LockVaultCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(LockVaultCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose clearance is declared with an attribute.
/// </summary>
[RequiresClearance("vaults.rotate")]
internal sealed class RotateVaultSealCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="RotateVaultSealCommand" />.
/// </summary>
internal sealed class RotateVaultSealCommandHandler : ICommandHandler<RotateVaultSealCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RotateVaultSealCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring no clearance, used to assert the undeclared decision.
/// </summary>
internal sealed class DraftVaultNoticeCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="DraftVaultNoticeCommand" />.
/// </summary>
internal sealed class DraftVaultNoticeCommandHandler : ICommandHandler<DraftVaultNoticeCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftVaultNoticeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that records why it needs no clearance rather than omitting one.
/// </summary>
[DeclarationExempt(typeof(RequiredClearance), "vault notices are public")]
internal sealed class BrowseVaultNoticesCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }
}

/// <summary>
///     Handles <see cref="BrowseVaultNoticesCommand" />.
/// </summary>
internal sealed class BrowseVaultNoticesCommandHandler : ICommandHandler<BrowseVaultNoticesCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowseVaultNoticesCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.HandlerRan = true;
        return Task.CompletedTask;
    }
}
