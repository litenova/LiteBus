using System;

namespace LiteBus.Auditing.Anchors.Timestamping;

/// <summary>
///     Thrown when an audit checkpoint cannot be time-stamped with RFC 3161: the checkpoint carries no usable
///     hash, the time-stamping authority cannot be reached, answers with an error, or returns a response the
///     request cannot be combined with.
/// </summary>
/// <remarks>
///     A signing failure fails the checkpoint cut, so the cut is retried and the missing timestamp is visible as
///     a missing checkpoint rather than as an unsigned one. Verification never throws this: a checkpoint whose
///     token is missing or unusable is reported through a <see langword="false" /> verdict.
/// </remarks>
public sealed class Rfc3161TimestampException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Rfc3161TimestampException" /> class.
    /// </summary>
    /// <param name="message">The message describing which checkpoint could not be time-stamped and why.</param>
    public Rfc3161TimestampException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="Rfc3161TimestampException" /> class.
    /// </summary>
    /// <param name="message">The message describing which checkpoint could not be time-stamped and why.</param>
    /// <param name="innerException">The transport or protocol exception the authority exchange failed with.</param>
    public Rfc3161TimestampException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
