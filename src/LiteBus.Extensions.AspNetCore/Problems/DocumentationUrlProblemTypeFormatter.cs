using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Formats problem types from a documentation URL template with a <c>{code}</c> placeholder.
/// </summary>
/// <remarks>
///     <para>
///         A refusal with a code answers the template with <c>{code}</c> replaced by the code, such as
///         <c>https://errors.example.com/validation.failed</c>. A refusal without a code answers the template with the
///         outcome identifier, such as <c>https://errors.example.com/mediation.invalid</c>.
///     </para>
/// </remarks>
public sealed class DocumentationUrlProblemTypeFormatter : IProblemTypeFormatter
{
    /// <summary>
    ///     The placeholder the template must contain.
    /// </summary>
    private const string CodePlaceholder = "{code}";

    /// <summary>
    ///     The documentation URL template with a <c>{code}</c> placeholder.
    /// </summary>
    private readonly string _template;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DocumentationUrlProblemTypeFormatter" /> class.
    /// </summary>
    /// <param name="template">The documentation URL template with a <c>{code}</c> placeholder.</param>
    public DocumentationUrlProblemTypeFormatter(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        if (!template.Contains(CodePlaceholder, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The documentation URL template must contain a '{code}' placeholder, such as 'https://errors.example.com/{code}'.",
                nameof(template));
        }

        _template = template;
    }

    /// <summary>
    ///     Gets the documentation URL template with a <c>{code}</c> placeholder.
    /// </summary>
    public string Template => _template;

    /// <inheritdoc />
    public string Format(string? code, MediationOutcome outcome)
    {
        var value = string.IsNullOrWhiteSpace(code) ? "mediation." + MediationOutcomeSlug.ToSlug(outcome) : code;

        return _template.Replace(CodePlaceholder, value, StringComparison.Ordinal);
    }
}
