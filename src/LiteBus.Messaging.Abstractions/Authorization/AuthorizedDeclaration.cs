using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that mediating a message requires authorization under a policy.
/// </summary>
/// <remarks>
///     Create it through <see cref="AuthorizationDeclaration.Authorized" />, so the required policy is always supplied.
///     LiteBus carries the policy to the application's guards and never evaluates it.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// AuthorizationDeclaration.Authorized("vaults.open")
/// ]]></code>
/// </example>
public sealed record AuthorizedDeclaration : AuthorizationDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizedDeclaration" /> class.
    /// </summary>
    /// <param name="policy">The policy the application authorizes the message against.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="policy" /> is null or whitespace.</exception>
    public AuthorizedDeclaration(string policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policy);
        Policy = policy;
    }

    /// <inheritdoc />
    public override bool RequiresAuthorization => true;

    /// <summary>
    ///     Gets the policy the application authorizes the message against, such as <c>vaults.open</c>.
    /// </summary>
    /// <remarks>
    ///     The policy is an application vocabulary LiteBus never interprets: a permission name, a role, or whatever the
    ///     application's guards read. It is required because an authorized message with no policy states nothing a
    ///     guard can enforce.
    /// </remarks>
    public string Policy { get; init; }
}
