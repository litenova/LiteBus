using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Formats the problem <c>type</c> URI for a refusal.
/// </summary>
public interface IProblemTypeFormatter
{
    /// <summary>
    ///     Formats the problem type for a refusal.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when none was supplied.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <returns>The problem type URI.</returns>
    string Format(string? code, MediationOutcome outcome);
}
