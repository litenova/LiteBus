using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Formats every problem type as <c>about:blank</c>, carrying no semantics beyond the status code.
/// </summary>
public sealed class AboutBlankProblemTypeFormatter : IProblemTypeFormatter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AboutBlankProblemTypeFormatter" /> class.
    /// </summary>
    public AboutBlankProblemTypeFormatter()
    {
    }

    /// <summary>
    ///     Gets the shared formatter used when the application configures the blank problem type.
    /// </summary>
    public static AboutBlankProblemTypeFormatter Default { get; } = new();

    /// <inheritdoc />
    public string Format(string? code, MediationOutcome outcome)
    {
        return "about:blank";
    }
}
