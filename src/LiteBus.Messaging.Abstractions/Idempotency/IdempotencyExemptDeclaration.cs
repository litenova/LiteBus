using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that a message is deliberately exempt from idempotency, and records why.
/// </summary>
/// <remarks>
///     An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the
///     selection of keyed messages reviewable. Create it through <see cref="IdempotencyDeclaration.Exempt" />. The
///     idempotency shortcut passes an exempt message through without claiming a key.
/// </remarks>
public sealed record IdempotencyExemptDeclaration : IdempotencyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyExemptDeclaration" /> class.
    /// </summary>
    /// <param name="rationale">The recorded reason a repeat of the message needs no recognition.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rationale" /> is null or whitespace.</exception>
    public IdempotencyExemptDeclaration(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Rationale = rationale;
    }

    /// <inheritdoc />
    public override bool RequiresIdempotency => false;

    /// <summary>
    ///     Gets the recorded reason a repeat of the message needs no recognition.
    /// </summary>
    public string Rationale { get; init; }
}
