namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The policy result that denies the message with a caller-facing refusal.
/// </summary>
/// <param name="Code">The stable refusal code carried on the denial.</param>
/// <param name="Reason">Why the message is denied, written for a person.</param>
/// <remarks>
///     The policy guard maps this to a denial verdict carrying the same code and reason.
/// </remarks>
public sealed record PolicyDeny(string Code, string Reason) : PolicyResult;
