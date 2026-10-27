namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     States the authorization position of a message: either it requires authorization under a policy, or it is deliberately exempt.
/// </summary>
/// <remarks>
///     <para>
///         This is a declaration, not an enforcement. LiteBus carries the position from the message to the metadata, and
///         never evaluates the policy: whether the current actor satisfies it stays in the application, typically in a
///         guard that reads the declaration through <see cref="IMessageMetadataAccessor" />.
///     </para>
///     <para>
///         The two positions are separate types rather than one type with a flag, so a declaration cannot hold a
///         combination that means nothing. An exemption has no policy; an authorized message has no exemption rationale.
///         Match on <see cref="AuthorizedDeclaration" /> and <see cref="AuthorizationExemptDeclaration" /> to tell them
///         apart.
///     </para>
///     <para>
///         Declare it either with <see cref="AuthorizedAttribute" /> and <see cref="AuthorizationExemptAttribute" /> on
///         the message, or with an <c>IAuthorizationDefinition&lt;TMessage&gt;</c> in a definition class beside the
///         message. A definition wins when both are present.
///     </para>
/// </remarks>
public abstract record AuthorizationDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizationDeclaration" /> class.
    /// </summary>
    /// <remarks>
    ///     The hierarchy is closed to the two positions LiteBus defines, so the constructor is not accessible outside
    ///     this assembly.
    /// </remarks>
    internal AuthorizationDeclaration()
    {
    }

    /// <summary>
    ///     Gets a value indicating whether mediating the message requires authorization.
    /// </summary>
    public abstract bool RequiresAuthorization { get; }

    /// <summary>
    ///     Creates a declaration for a message that requires authorization under a policy.
    /// </summary>
    /// <param name="policy">The policy the application authorizes the message against, such as <c>vaults.open</c>.</param>
    /// <returns>The authorized declaration.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="policy" /> is null or whitespace.</exception>
    public static AuthorizedDeclaration Authorized(string policy)
    {
        return new AuthorizedDeclaration(policy);
    }

    /// <summary>
    ///     Creates a declaration for a message that is deliberately exempt from authorization.
    /// </summary>
    /// <param name="rationale">The recorded reason the message needs no authorization.</param>
    /// <returns>The exempt declaration.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public static AuthorizationExemptDeclaration Exempt(string rationale)
    {
        return new AuthorizationExemptDeclaration(rationale);
    }
}
