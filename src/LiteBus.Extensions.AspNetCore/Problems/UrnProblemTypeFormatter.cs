using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Formats problem types as stable <c>urn:litebus:</c> identifiers derived from the refusal code.
/// </summary>
/// <remarks>
///     <para>
///         A refusal with a code answers <c>urn:litebus:</c> plus the code with dots replaced by colons, so one client
///         handler reads every refusal the same way. A refusal without a code answers the outcome identifier, such as
///         <c>urn:litebus:mediation:invalid</c>.
///     </para>
/// </remarks>
public sealed class UrnProblemTypeFormatter : IProblemTypeFormatter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="UrnProblemTypeFormatter" /> class.
    /// </summary>
    public UrnProblemTypeFormatter()
    {
    }

    /// <summary>
    ///     Gets the shared formatter used unless the application configures another one.
    /// </summary>
    public static UrnProblemTypeFormatter Default { get; } = new();

    /// <inheritdoc />
    public string Format(string? code, MediationOutcome outcome)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            return "urn:litebus:" + code.Replace(".", ":", StringComparison.Ordinal);
        }

        return "urn:litebus:mediation:" + MediationOutcomeSlug.ToSlug(outcome);
    }
}
