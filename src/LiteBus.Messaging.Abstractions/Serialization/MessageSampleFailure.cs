using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Describes why a message type could not be sampled for a host validation round trip.
/// </summary>
/// <param name="FailedType">The type that could not be sampled.</param>
/// <param name="MemberPath">
///     The member path from the message root to the unsampleable member, such as
///     <c>Lines[0].Price</c>, or the empty string when the message type itself cannot be sampled.
/// </param>
/// <param name="Reason">Why the type cannot be sampled, in the words the validation failure uses.</param>
/// <remarks>
///     <para>
///         Answered by <see cref="IMessageSampleFactory.DescribeFailureAsync" /> when
///         <see cref="IMessageSampleFactory.CreateSampleAsync" /> answers
///         <see langword="null" />. The round-trip check renders the path, the type, and the reason
///         into the failure so the registration that fixes it is one line away.
///     </para>
/// </remarks>
public sealed record MessageSampleFailure(
    Type FailedType,
    string MemberPath,
    string Reason);
