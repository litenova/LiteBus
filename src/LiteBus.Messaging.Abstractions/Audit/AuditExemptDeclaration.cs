using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that a message is deliberately not audited, and records why.
/// </summary>
/// <remarks>
///     An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the
///     selection of audited events reviewable, and is what auditing standards ask for when they require event selection
///     to be documented. Create it through <see cref="AuditDeclaration.Exempt" />.
/// </remarks>
public sealed record AuditExemptDeclaration : AuditDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditExemptDeclaration" /> class.
    /// </summary>
    /// <param name="rationale">The recorded reason the message is exempt from auditing.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public AuditExemptDeclaration(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Rationale = rationale;
    }

    /// <inheritdoc />
    public override bool IsAudited => false;

    /// <summary>
    ///     Gets the recorded reason the message is not audited.
    /// </summary>
    public string Rationale { get; init; }
}
