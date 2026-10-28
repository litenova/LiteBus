using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that a message is deliberately exempt from authorization, and records why.
/// </summary>
/// <remarks>
///     An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the
///     selection of authorized messages reviewable. Create it through <see cref="AuthorizationDeclaration.Exempt" />.
/// </remarks>
public sealed record AuthorizationExemptDeclaration : AuthorizationDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuthorizationExemptDeclaration" /> class.
    /// </summary>
    /// <param name="rationale">The recorded reason the message needs no authorization.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public AuthorizationExemptDeclaration(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Rationale = rationale;
    }

    /// <inheritdoc />
    public override bool RequiresAuthorization => false;

    /// <summary>
    ///     Gets the recorded reason the message needs no authorization.
    /// </summary>
    public string Rationale { get; init; }
}
