using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Writes validation failures as an array of one object per failure, carrying the member, the code, and the message.
/// </summary>
/// <remarks>
///     <para>
///         Writes <c>fields</c> as one object per failure, carrying the member as <c>field</c>, the failure code as
///         <c>code</c>, and the message as <c>message</c>, so a client reads per-field messages and per-field codes
///         from one object. Message-level failures carry a <see langword="null" /> field, and uncoded failures carry
///         a <see langword="null" /> code. <see cref="FieldValidationErrorsWriterOptions" /> renames the extension
///         and the entry members.
///     </para>
/// </remarks>
public sealed class FieldValidationErrorsWriter : IValidationErrorsWriter
{
    /// <summary>
    ///     The member names the written entries use.
    /// </summary>
    private readonly FieldValidationErrorsWriterOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="FieldValidationErrorsWriter" /> class with default member names.
    /// </summary>
    public FieldValidationErrorsWriter()
        : this(new FieldValidationErrorsWriterOptions())
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="FieldValidationErrorsWriter" /> class.
    /// </summary>
    /// <param name="options">The member names the written entries use.</param>
    public FieldValidationErrorsWriter(FieldValidationErrorsWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        _options = options;
    }

    /// <summary>
    ///     Gets the shared writer used when the application configures the object-per-field validation shape.
    /// </summary>
    public static FieldValidationErrorsWriter Default { get; } = new();

    /// <summary>
    ///     Gets the member names the written entries use.
    /// </summary>
    public FieldValidationErrorsWriterOptions Options => _options;

    /// <inheritdoc />
    public void WriteErrors(ProblemDetails problem, IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(failures);

        if (failures.Count == 0)
        {
            return;
        }

        ValidateOptions(_options);

        var entries = new List<Dictionary<string, string?>>(failures.Count);

        foreach (var failure in failures)
        {
            entries.Add(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [_options.FieldMemberName] = failure.Member,
                [_options.CodeMemberName] = failure.Code,
                [_options.MessageMemberName] = failure.Message,
            });
        }

        problem.Extensions[_options.ExtensionName] = entries;
    }

    /// <summary>
    ///     Validates the member names the writer writes with.
    /// </summary>
    /// <param name="options">The member names the written entries use.</param>
    private static void ValidateOptions(FieldValidationErrorsWriterOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExtensionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FieldMemberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CodeMemberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.MessageMemberName);

        if (string.Equals(options.FieldMemberName, options.CodeMemberName, StringComparison.Ordinal)
            || string.Equals(options.FieldMemberName, options.MessageMemberName, StringComparison.Ordinal)
            || string.Equals(options.CodeMemberName, options.MessageMemberName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The field, code, and message member names must differ from each other.",
                nameof(options));
        }
    }
}
