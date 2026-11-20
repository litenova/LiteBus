using System;
using System.Collections.Generic;

namespace LiteBus.Testing;

/// <summary>
///     Thrown when a LiteBus host composition fails validation because one or more of its registrations cannot be built
///     or resolved.
/// </summary>
/// <remarks>
///     The <see cref="Failures" /> list holds one entry per problem, so a caller can report every composition mistake
///     from a single validation run instead of fixing them one at a time.
/// </remarks>
public sealed class LiteBusHostValidationException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostValidationException" /> class with the collected failures.
    /// </summary>
    /// <param name="failures">The descriptions of the registrations that failed validation.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="failures" /> is <see langword="null" />.
    /// </exception>
    public LiteBusHostValidationException(IReadOnlyList<string> failures)
        : base(BuildMessage(failures))
    {
        Failures = [.. failures];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostValidationException" /> class.
    /// </summary>
    public LiteBusHostValidationException()
    {
        Failures = [];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostValidationException" /> class with a message.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public LiteBusHostValidationException(string message)
        : base(message)
    {
        Failures = [];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusHostValidationException" /> class with a message and the
    ///     exception that caused it.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The exception that caused this validation failure.</param>
    public LiteBusHostValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = [];
    }

    /// <summary>
    ///     Gets the descriptions of the registrations that failed validation.
    /// </summary>
    /// <value>One entry for every problem found during the validation run.</value>
    public IReadOnlyList<string> Failures { get; }

    /// <summary>
    ///     Builds the exception message from the collected failures.
    /// </summary>
    /// <param name="failures">The descriptions of the registrations that failed validation.</param>
    /// <returns>A header naming the problem count followed by one failure per line.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="failures" /> is <see langword="null" />.
    /// </exception>
    private static string BuildMessage(IReadOnlyList<string> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var lines = new List<string>(failures.Count + 1)
        {
            $"The LiteBus host failed validation with {failures.Count} problem(s):"
        };
        lines.AddRange(failures);

        return string.Join(Environment.NewLine, lines);
    }
}
