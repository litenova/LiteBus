using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that the annotated message is deliberately exempt from authorization, and records why.
/// </summary>
/// <remarks>
///     <para>
///         An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the
///         selection of authorized messages reviewable.
///     </para>
///     <para>
///         Pair with <see cref="AuthorizedAttribute" />, so a message carrying neither can be reported as undeclared
///         rather than silently going unauthorized.
///     </para>
///     <para>
///         This is shorthand for <c>[DeclarationExempt(typeof(AuthorizationDeclaration), rationale)]</c> and records the
///         same exemption, so every exemption a message carries is readable from one
///         <see cref="DeclarationExemptions" /> value however it was written. It exists because authorization is, like
///         auditing, a declaration whose two positions are both modelled as values, so an exempt message also
///         contributes an <see cref="AuthorizationExemptDeclaration" /> a guard reads.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [AuthorizationExempt("the vault catalogue is public, so there is no actor to authorize")]
/// public sealed record BrowseVaultCatalogueQuery : IQuery<VaultCatalogueView>;
/// ]]></code>
/// </example>
[MessageDeclaration(typeof(AuthorizationDeclaration))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AuthorizationExemptAttribute : Attribute, IMessageDeclarationSource, IMessageDeclarationExemptionSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizationExemptAttribute" /> class.
    /// </summary>
    /// <param name="rationale">The recorded reason the message needs no authorization.</param>
    /// <exception cref="ArgumentException"><paramref name="rationale" /> is null, empty, or whitespace.</exception>
    public AuthorizationExemptAttribute(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Rationale = rationale;
    }

    /// <summary>
    ///     Gets the recorded reason the message needs no authorization.
    /// </summary>
    public string Rationale { get; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(AuthorizationDeclaration);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return AuthorizationDeclaration.Exempt(Rationale);
    }

    /// <inheritdoc />
    public DeclarationExemption CreateExemption()
    {
        return new DeclarationExemption(typeof(AuthorizationDeclaration), Rationale);
    }
}
