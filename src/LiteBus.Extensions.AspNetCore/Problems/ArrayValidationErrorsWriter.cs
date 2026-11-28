using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Writes validation failures as the RFC 9457 <c>errors</c> array of one entry per failure.
/// </summary>
/// <remarks>
///     <para>
///         Writes <c>errors</c> as one <see cref="ValidationErrorEntry" /> per failure, carrying the message as
///         <c>detail</c>, the member as a JSON pointer, and the failure code. Message-level failures carry a
///         <see langword="null" /> pointer, and uncoded failures carry a <see langword="null" /> code.
///     </para>
/// </remarks>
public sealed class ArrayValidationErrorsWriter : IValidationErrorsWriter
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ArrayValidationErrorsWriter" /> class.
    /// </summary>
    public ArrayValidationErrorsWriter()
    {
    }

    /// <summary>
    ///     Gets the shared writer used when the application configures the array validation shape.
    /// </summary>
    public static ArrayValidationErrorsWriter Default { get; } = new();

    /// <inheritdoc />
    public void WriteErrors(ProblemDetails problem, IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 0)
        {
            return;
        }

        var entries = new List<ValidationErrorEntry>(failures.Count);

        foreach (var failure in failures)
        {
            entries.Add(new ValidationErrorEntry(failure.Message, ToPointer(failure.Member), failure.Code));
        }

        problem.Extensions["errors"] = entries;
    }

    /// <summary>
    ///     Converts a failure member to the JSON pointer of the invalid value.
    /// </summary>
    /// <param name="member">The part of the message the failure applies to, when it applies to one.</param>
    /// <returns>The JSON pointer, or <see langword="null" /> when the failure is about the message as a whole.</returns>
    private static string? ToPointer(string? member)
    {
        if (member is null)
        {
            return null;
        }

        return "/" + member.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }
}
