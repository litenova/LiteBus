using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Guards a message by reading one declared metadata value and deciding from it.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <typeparam name="TValue">The metadata value type this guard reads, which is also its metadata key.</typeparam>
/// <remarks>
///     <para>
///         Every application with a declaration such as a required permission writes the same guard: look the declaration
///         up through <see cref="IMessageMetadataAccessor" />, decide when it is missing, call the real check. This class
///         is that guard, so a subclass implements only the check itself in
///         <see cref="DecideAsync(TValue, TMessage, CancellationToken)" />.
///     </para>
///     <para>
///         The lookup reads the runtime type of the message, not <typeparamref name="TMessage" />, so an open generic
///         guard closed over a base type still enforces the concrete message's declaration. A guard closed over a base
///         type that read the base type instead would enforce one declaration for every message beneath it.
///     </para>
///     <para>
///         A message that declares the value reaches <see cref="DecideAsync(TValue, TMessage, CancellationToken)" />. A
///         message that records an exemption for <typeparamref name="TValue" /> in
///         <see cref="DeclarationExemptions" /> is allowed without calling it, because an exemption is a decision the
///         message already made. A message that declares neither reaches
///         <see cref="DecideUndeclaredAsync(TMessage, CancellationToken)" />, which denies by default.
///     </para>
///     <para>
///         Register a subclass once as an open generic and it covers every message it constrains, or register it closed
///         over one message when the check is message-specific. Both forms are discovered like any other guard.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class PermissionGuard<TCommand> : DeclarationGuard<TCommand, RequiredPermission>
///     where TCommand : ICommand
/// {
///     private readonly IAccessAuthorizer _authorizer;
///
///     public PermissionGuard(IMessageMetadataAccessor metadata, IAccessAuthorizer authorizer)
///         : base(metadata)
///     {
///         _authorizer = authorizer;
///     }
///
///     protected override Task<Verdict> DecideAsync(
///         RequiredPermission declaration,
///         TCommand message,
///         CancellationToken cancellationToken)
///     {
///         return Task.FromResult(_authorizer.Holds(declaration)
///             ? Verdict.Allow
///             : Verdict.Deny($"the caller does not hold {declaration.Name}"));
///     }
/// }
/// ]]></code>
/// </example>
public abstract class DeclarationGuard<TMessage, TValue> : IMessageGuard<TMessage>
    where TMessage : notnull
    where TValue : class
{
    /// <summary>
    ///     The code carried by the denial <see cref="DecideUndeclaredAsync(TMessage, CancellationToken)" /> returns by
    ///     default, so a refusal mapper can tell a missing declaration apart from a failed check.
    /// </summary>
    public const string UndeclaredCode = "declaration.missing";

    /// <summary>
    ///     Reads the declared metadata value for the message being mediated.
    /// </summary>
    private readonly IMessageMetadataAccessor _metadataAccessor;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationGuard{TMessage, TValue}" /> class.
    /// </summary>
    /// <param name="metadataAccessor">Reads the declared metadata value for the message being mediated.</param>
    public DeclarationGuard(IMessageMetadataAccessor metadataAccessor)
    {
        ArgumentNullException.ThrowIfNull(metadataAccessor);

        _metadataAccessor = metadataAccessor;
    }

    /// <summary>
    ///     Decides whether the message is permitted to proceed by reading its declared value.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    /// <remarks>
    ///     A declared value is passed to <see cref="DecideAsync(TValue, TMessage, CancellationToken)" />. A recorded
    ///     exemption for <typeparamref name="TValue" /> allows without calling it. Anything else reaches
    ///     <see cref="DecideUndeclaredAsync(TMessage, CancellationToken)" />.
    /// </remarks>
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var messageType = message.GetType();

        if (_metadataAccessor.TryGet<TValue>(messageType, out var declaration))
        {
            return DecideAsync(declaration, message, cancellationToken);
        }

        if (_metadataAccessor.TryGet<DeclarationExemptions>(messageType, out var exemptions)
            && exemptions.Covers<TValue>())
        {
            return Task.FromResult(Verdict.Allow);
        }

        return DecideUndeclaredAsync(message, cancellationToken);
    }

    /// <summary>
    ///     Decides whether a message carrying a declared value is permitted to proceed.
    /// </summary>
    /// <param name="declaration">The declared value read from the message.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    protected abstract Task<Verdict> DecideAsync(TValue declaration, TMessage message, CancellationToken cancellationToken);

    /// <summary>
    ///     Decides whether a message declaring no value is permitted to proceed.
    /// </summary>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    /// <remarks>
    ///     The default denies with <see cref="UndeclaredCode" />, because a message nobody considered must not pass a
    ///     check nobody wrote it for. Override to allow while a declaration is still being rolled out across an existing
    ///     codebase, and pair the guard with a composition requirement over <typeparamref name="TValue" /> so the
    ///     override becomes unreachable once every message declares its position.
    /// </remarks>
    protected virtual Task<Verdict> DecideUndeclaredAsync(TMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var reason = $"The message '{message.GetType().Name}' declares no '{typeof(TValue).Name}'.";

        return Task.FromResult(Verdict.Deny(reason, UndeclaredCode));
    }
}
