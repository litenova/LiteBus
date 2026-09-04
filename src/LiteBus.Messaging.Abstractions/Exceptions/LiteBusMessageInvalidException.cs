using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a message rejected by a validator, raised when no refusal mapper supplies a value for the caller.
/// </summary>
/// <remarks>
///     <para>
///         A validation failure is a decision, not a fault, so it does not reach error handlers and is not reported as
///         <see cref="MediationOutcome.Failed" />. The mediation reports <see cref="MediationOutcome.Invalid" />, completion
///         handlers observe it, and this exception then reaches the caller because a method that must return a value has
///         nothing to return.
///     </para>
///     <para>
///         It is kept apart from <see cref="LiteBusMessageDeniedException" /> because a malformed message is not a
///         refused one. An audit trail records a denial in the list a security review reads, and a validation failure
///         belongs in neither that list nor the failure list.
///     </para>
///     <para>
///         <see cref="Failures" /> carries every failure the stage collected, because validators aggregate: a caller
///         fixing a malformed message wants all of them at once.
///     </para>
/// </remarks>
public sealed class LiteBusMessageInvalidException : Exception
{
    /// <summary>
    ///     The empty list used when the exception was constructed without failures.
    /// </summary>
    private static readonly IReadOnlyList<ValidationFailure> NoFailures = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageInvalidException" /> class.
    /// </summary>
    /// <param name="messageType">The type of the message that was rejected.</param>
    /// <param name="failures">The failures the validator stage collected.</param>
    public LiteBusMessageInvalidException(Type messageType, IReadOnlyList<ValidationFailure> failures)
        : base(BuildMessage(messageType, failures))
    {
        MessageType = messageType;
        Failures = failures ?? NoFailures;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageInvalidException" /> class with a default message.
    /// </summary>
    public LiteBusMessageInvalidException()
        : base("The message is invalid.")
    {
        Failures = NoFailures;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageInvalidException" /> class with the given message.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    public LiteBusMessageInvalidException(string message)
        : base(message)
    {
        Failures = NoFailures;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="LiteBusMessageInvalidException" /> class with the given message
    ///     and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the failure.</param>
    /// <param name="innerException">The exception that caused the failure.</param>
    public LiteBusMessageInvalidException(string message, Exception innerException)
        : base(message, innerException)
    {
        Failures = NoFailures;
    }

    /// <summary>
    ///     Gets the type of the message that was rejected, when the rejection came from a validator.
    /// </summary>
    public Type? MessageType { get; }

    /// <summary>
    ///     Gets every failure the validator stage collected.
    /// </summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    /// <summary>
    ///     Builds the exception message from the collected failures.
    /// </summary>
    /// <param name="messageType">The type of the message that was rejected.</param>
    /// <param name="failures">The failures the validator stage collected.</param>
    /// <returns>A message naming the message type and listing every failure.</returns>
    private static string BuildMessage(Type messageType, IReadOnlyList<ValidationFailure>? failures)
    {
        if (failures is null || failures.Count == 0)
        {
            return $"Mediation of '{messageType?.Name}' was stopped because the message is invalid.";
        }

        var listed = string.Join("; ", failures.Select(failure => failure.ToString()));

        return $"Mediation of '{messageType?.Name}' was stopped because the message is invalid: {listed}";
    }
}
