using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Metadata;

/// <summary>
///     A permission an application declares on a message, standing in for any consumer-defined declaration.
/// </summary>
/// <param name="Name">The permission identifier.</param>
public sealed record RequiredPermission(string Name);

/// <summary>
///     A second consumer-defined declaration, used to assert that absence is reported rather than failing.
/// </summary>
/// <param name="Days">How long the record is kept.</param>
public sealed record RetentionClass(int Days);

/// <summary>
///     Declares the permission a message requires as an attribute rather than a definition class.
/// </summary>
/// <remarks>
///     It implements <see cref="IMessageDeclarationSource" />, which is what puts the value into the same type-keyed
///     bag a definition writes to. An attribute that does not implement it is not metadata and is never collected.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPermissionAttribute : Attribute, IMessageDeclarationSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RequiresPermissionAttribute" /> class.
    /// </summary>
    /// <param name="permission">The permission identifier.</param>
    public RequiresPermissionAttribute(string permission)
    {
        Permission = permission;
    }

    /// <summary>
    ///     Gets the permission identifier.
    /// </summary>
    public string Permission { get; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(RequiredPermission);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return new RequiredPermission(Permission);
    }
}

/// <summary>
///     Reports which permissions the calling actor holds.
/// </summary>
public interface ICurrentActor
{
    /// <summary>
    ///     Determines whether the actor holds the given permission.
    /// </summary>
    /// <param name="permission">The permission to check.</param>
    /// <returns><see langword="true" /> when the actor holds it.</returns>
    bool Holds(RequiredPermission permission);
}

/// <summary>
///     An actor holding a fixed set of permissions.
/// </summary>
public sealed class FakeActor : ICurrentActor
{
    /// <summary>
    ///     The permission names the actor holds.
    /// </summary>
    private readonly HashSet<string> _held;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FakeActor" /> class.
    /// </summary>
    /// <param name="held">The permission names the actor holds.</param>
    public FakeActor(params string[] held)
    {
        _held = [..held];
    }

    /// <inheritdoc />
    public bool Holds(RequiredPermission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return _held.Contains(permission.Name);
    }
}

/// <summary>
///     One guard covering every message that declares a required permission.
/// </summary>
/// <typeparam name="TCommand">The command type the guard is closed over at registration.</typeparam>
/// <remarks>
///     This is the shape <see cref="IMessageMetadataAccessor" /> exists for: the declaration says what the message
///     needs, and one guard enforces it everywhere instead of one guard per message.
/// </remarks>
internal sealed class PermissionGuard<TCommand> : ICommandGuard<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     Reports which permissions the calling actor holds.
    /// </summary>
    private readonly ICurrentActor _actor;

    /// <summary>
    ///     Reads the declaration from the message type.
    /// </summary>
    private readonly IMessageMetadataAccessor _metadata;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PermissionGuard{TCommand}" /> class.
    /// </summary>
    /// <param name="metadata">Reads the declaration from the message type.</param>
    /// <param name="actor">Reports which permissions the calling actor holds.</param>
    public PermissionGuard(IMessageMetadataAccessor metadata, ICurrentActor actor)
    {
        _metadata = metadata;
        _actor = actor;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        if (!_metadata.TryGet<TCommand, RequiredPermission>(out var required))
        {
            return Task.FromResult(Verdict.Allow);
        }

        return Task.FromResult(_actor.Holds(required)
            ? Verdict.Allow
            : Verdict.Deny($"the caller does not hold {required.Name}"));
    }
}

/// <summary>
///     A command whose permission and audit position are declared in a definition class.
/// </summary>
internal sealed class PublishScheduleCommand : ICommand;

/// <summary>
///     Declares both a LiteBus value and an application value for the same message.
/// </summary>
/// <remarks>
///     One class, two declarations, keyed by the value type. It is what makes the accessor useful beyond auditing: the
///     permission and the audit position are read back through the same call.
/// </remarks>
internal sealed class PublishScheduleCommandDefinition :
    IAuditDefinition<PublishScheduleCommand>,
    IMessageDefinition<PublishScheduleCommand, RequiredPermission>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("schedules.publish");

    /// <inheritdoc />
    public RequiredPermission Value => new("schedules.publish");
}

/// <summary>
///     Handles <see cref="PublishScheduleCommand" />.
/// </summary>
internal sealed class PublishScheduleCommandHandler : ICommandHandler<PublishScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(PublishScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose permission is declared with an attribute.
/// </summary>
[RequiresPermission("schedules.touch")]
internal sealed class TouchScheduleCommand : ICommand;

/// <summary>
///     Handles <see cref="TouchScheduleCommand" />.
/// </summary>
internal sealed class TouchScheduleCommandHandler : ICommandHandler<TouchScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(TouchScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command declaring nothing, used to assert the requirement fails composition.
/// </summary>
internal sealed class DraftScheduleCommand : ICommand;

/// <summary>
///     Handles <see cref="DraftScheduleCommand" />.
/// </summary>
internal sealed class DraftScheduleCommandHandler : ICommandHandler<DraftScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(DraftScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A second command declaring nothing, so the composition error can be checked for listing both.
/// </summary>
internal sealed class WithdrawScheduleCommand : ICommand;

/// <summary>
///     Handles <see cref="WithdrawScheduleCommand" />.
/// </summary>
internal sealed class WithdrawScheduleCommandHandler : ICommandHandler<WithdrawScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(WithdrawScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that records why it needs no permission rather than omitting one.
/// </summary>
[DeclarationExempt(typeof(RequiredPermission), "the schedule list is public")]
internal sealed class BrowseScheduleCommand : ICommand;

/// <summary>
///     Handles <see cref="BrowseScheduleCommand" />.
/// </summary>
internal sealed class BrowseScheduleCommandHandler : ICommandHandler<BrowseScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(BrowseScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command exempt from a different declaration, which says nothing about the required one.
/// </summary>
[DeclarationExempt(typeof(RetentionClass), "nothing is retained")]
internal sealed class RetireScheduleCommand : ICommand;

/// <summary>
///     Handles <see cref="RetireScheduleCommand" />.
/// </summary>
internal sealed class RetireScheduleCommandHandler : ICommandHandler<RetireScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(RetireScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command carrying two exemptions, which have to aggregate into one metadata value.
/// </summary>
[DeclarationExempt(typeof(RequiredPermission), "internal maintenance command")]
[DeclarationExempt(typeof(RetentionClass), "nothing is retained")]
internal sealed class ArchiveScheduleCommand : ICommand;

/// <summary>
///     Handles <see cref="ArchiveScheduleCommand" />.
/// </summary>
internal sealed class ArchiveScheduleCommandHandler : ICommandHandler<ArchiveScheduleCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ArchiveScheduleCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
