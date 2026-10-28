using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes one thing a validator found wrong with a message.
/// </summary>
/// <remarks>
///     A failure names what is wrong and, when it applies to one part of the message, which part. LiteBus does not
///     interpret <see cref="Member" /> or <see cref="Code" />; they exist so a caller, a refusal mapper, or a transport
///     layer can present the failure without parsing <see cref="Message" />, which is prose written for a person.
/// </remarks>
public readonly struct ValidationFailure : IEquatable<ValidationFailure>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ValidationFailure" /> struct.
    /// </summary>
    /// <param name="message">What is wrong, written for a person.</param>
    /// <param name="member">The part of the message the failure applies to, when it applies to one.</param>
    /// <param name="code">A machine-readable code for the failure, when the validator supplies one.</param>
    /// <exception cref="ArgumentException"><paramref name="message" /> is null, empty, or whitespace.</exception>
    public ValidationFailure(string message, string? member = null, string? code = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Message = message;
        Member = member;
        Code = code;
    }

    /// <summary>
    ///     Gets the description of what is wrong.
    /// </summary>
    /// <value>Prose written for a person; a failure always carries one.</value>
    public string Message { get; }

    /// <summary>
    ///     Gets the part of the message this failure applies to.
    /// </summary>
    /// <value>The member name, or <see langword="null" /> when the failure is about the message as a whole.</value>
    public string? Member { get; }

    /// <summary>
    ///     Gets the machine-readable code for this failure.
    /// </summary>
    /// <value>The code, or <see langword="null" /> when the validator supplied none.</value>
    public string? Code { get; }

    /// <summary>
    ///     Determines whether two failures are equal.
    /// </summary>
    /// <param name="left">The first failure.</param>
    /// <param name="right">The second failure.</param>
    /// <returns><see langword="true" /> when both describe the same failure.</returns>
    public static bool operator ==(ValidationFailure left, ValidationFailure right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two failures differ.
    /// </summary>
    /// <param name="left">The first failure.</param>
    /// <param name="right">The second failure.</param>
    /// <returns><see langword="true" /> when they describe different failures.</returns>
    public static bool operator !=(ValidationFailure left, ValidationFailure right)
    {
        return !left.Equals(right);
    }

    /// <inheritdoc />
    public bool Equals(ValidationFailure other)
    {
        return string.Equals(Message, other.Message, StringComparison.Ordinal)
               && string.Equals(Member, other.Member, StringComparison.Ordinal)
               && string.Equals(Code, other.Code, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is ValidationFailure other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(Message, Member, Code);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Member is null ? Message : $"{Member}: {Message}";
    }
}
