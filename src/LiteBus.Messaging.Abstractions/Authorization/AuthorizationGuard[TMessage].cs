using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Guards a message by enforcing its declared authorization position.
/// </summary>
/// <typeparam name="TMessage">The type of message this guard runs for.</typeparam>
/// <remarks>
///     <para>
///         The guard reads the message's <see cref="AuthorizationDeclaration" /> through the shared
///         <see cref="DeclarationGuard{TMessage, TValue}" /> lookup, so it follows the same rules: the runtime message
///         type is read, a recorded exemption allows, and a message declaring nothing is denied with
///         <see cref="DeclarationGuard{TMessage, AuthorizationDeclaration}.UndeclaredCode" /> unless a subclass overrides
///         <see cref="DeclarationGuard{TMessage, AuthorizationDeclaration}.DecideUndeclaredAsync(TMessage, CancellationToken)" />.
///     </para>
///     <para>
///         An <see cref="AuthorizationExemptDeclaration" /> allows without calling anything, because the message already
///         decided it needs no authorization. An <see cref="AuthorizedDeclaration" /> reaches
///         <see cref="AuthorizeAsync(string, TMessage, CancellationToken)" /> with the declared policy, which is where
///         the application answers whether the current actor satisfies it. LiteBus carries the policy and never
///         evaluates it.
///     </para>
///     <para>
///         Register a subclass once as an open generic and it covers every message it constrains, or register it closed
///         over one message when the check is message-specific. Both forms are discovered like any other guard.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class VaultAuthorizationGuard<TCommand> : AuthorizationGuard<TCommand>
///     where TCommand : ICommand
/// {
///     private readonly IAccessAuthorizer _authorizer;
///
///     public VaultAuthorizationGuard(IMessageMetadataAccessor metadata, IAccessAuthorizer authorizer)
///         : base(metadata)
///     {
///         _authorizer = authorizer;
///     }
///
///     protected override Task<Verdict> AuthorizeAsync(
///         string policy,
///         TCommand message,
///         CancellationToken cancellationToken)
///     {
///         return Task.FromResult(_authorizer.Holds(policy)
///             ? Verdict.Allow
///             : Verdict.Deny($"the caller is not authorized under {policy}"));
///     }
/// }
/// ]]></code>
/// </example>
public abstract class AuthorizationGuard<TMessage> : DeclarationGuard<TMessage, AuthorizationDeclaration>
    where TMessage : notnull
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizationGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="metadataAccessor">Reads the authorization declaration for the message being mediated.</param>
    public AuthorizationGuard(IMessageMetadataAccessor metadataAccessor)
        : base(metadataAccessor)
    {
    }

    /// <summary>
    ///     Decides whether a message carrying an authorization declaration is permitted to proceed.
    /// </summary>
    /// <param name="declaration">The authorization declaration read from the message.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    /// <remarks>
    ///     An <see cref="AuthorizationExemptDeclaration" /> allows. An <see cref="AuthorizedDeclaration" /> delegates to
    ///     <see cref="AuthorizeAsync(string, TMessage, CancellationToken)" /> with the declared policy.
    /// </remarks>
    protected sealed override Task<Verdict> DecideAsync(
        AuthorizationDeclaration declaration,
        TMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(message);

        if (declaration is AuthorizationExemptDeclaration)
        {
            return Task.FromResult(Verdict.Allow);
        }

        return AuthorizeAsync(((AuthorizedDeclaration) declaration).Policy, message, cancellationToken);
    }

    /// <summary>
    ///     Decides whether the current actor satisfies the declared policy for the message.
    /// </summary>
    /// <param name="policy">The policy the message declares, in the application's own vocabulary.</param>
    /// <param name="message">The message being mediated.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>The verdict that allows or denies the message.</returns>
    protected abstract Task<Verdict> AuthorizeAsync(string policy, TMessage message, CancellationToken cancellationToken);
}
