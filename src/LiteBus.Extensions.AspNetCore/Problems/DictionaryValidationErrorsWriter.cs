using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Writes validation failures as the <c>errors</c> dictionary ASP.NET Core clients already render.
/// </summary>
/// <remarks>
///     <para>
///         Writes <c>errors</c> keyed by member, with message-level failures under an empty key, in the same shape as
///         <c>ValidationProblemDetails.Errors</c>. Writes <c>errorCodes</c> beside it, keyed by the same members with
///         each failure code aligned by index to its message, so clients read per-failure codes without parsing prose.
///     </para>
/// </remarks>
public sealed class DictionaryValidationErrorsWriter : IValidationErrorsWriter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DictionaryValidationErrorsWriter" /> class.
    /// </summary>
    public DictionaryValidationErrorsWriter()
    {
    }

    /// <summary>
    ///     Gets the shared writer used unless the application configures another one.
    /// </summary>
    public static DictionaryValidationErrorsWriter Default { get; } = new();

    /// <inheritdoc />
    public void WriteErrors(ProblemDetails problem, IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 0)
        {
            return;
        }

        var grouped = new Dictionary<string, List<ValidationFailure>>(StringComparer.Ordinal);

        foreach (var failure in failures)
        {
            var key = failure.Member ?? string.Empty;

            if (!grouped.TryGetValue(key, out var members))
            {
                members = [];
                grouped[key] = members;
            }

            members.Add(failure);
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var codes = new Dictionary<string, string?[]>(StringComparer.Ordinal);

        foreach (var (key, members) in grouped)
        {
            var messages = new string[members.Count];
            var memberCodes = new string?[members.Count];

            for (var index = 0; index < members.Count; index++)
            {
                messages[index] = members[index].Message;
                memberCodes[index] = members[index].Code;
            }

            errors[key] = messages;
            codes[key] = memberCodes;
        }

        problem.Extensions["errors"] = errors;
        problem.Extensions["errorCodes"] = codes;
    }
}
