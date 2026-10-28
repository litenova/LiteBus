using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that mediating the annotated message requires authorization under a policy.
/// </summary>
/// <remarks>
///     <para>
///         The attribute is the lightweight form of an authorization declaration, for messages whose configuration is a
///         single fact. When a message needs richer or compile-checked configuration, declare an
///         <c>IAuthorizationDefinition&lt;TMessage&gt;</c> in a definition class instead. A definition wins when both are
///         present, because both contribute the same <see cref="AuthorizationDeclaration" /> and the definition is
///         applied last.
///     </para>
///     <para>
///         Pair with <see cref="AuthorizationExemptAttribute" /> so that every message states its position explicitly,
///         rather than an unauthorized message being indistinguishable from one nobody considered.
///     </para>
///     <para>
///         The name stays <c>Authorized</c> rather than <c>Authorize</c>, which is ASP.NET Core's. This attribute
///         declares a position LiteBus carries; it authorizes nothing.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [Authorized("vaults.open")]
/// public sealed record OpenVaultCommand(Guid VaultId) : ICommand;
/// ]]></code>
/// </example>
[MessageDeclaration(typeof(AuthorizationDeclaration))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AuthorizedAttribute : Attribute, IMessageDeclarationSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizedAttribute" /> class.
    /// </summary>
    /// <param name="policy">The policy the application authorizes the message against, such as <c>vaults.open</c>.</param>
    public AuthorizedAttribute(string policy)
    {
        Policy = policy;
    }

    /// <summary>
    ///     Gets the policy the application authorizes the message against.
    /// </summary>
    public string Policy { get; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(AuthorizationDeclaration);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return AuthorizationDeclaration.Authorized(Policy);
    }
}
