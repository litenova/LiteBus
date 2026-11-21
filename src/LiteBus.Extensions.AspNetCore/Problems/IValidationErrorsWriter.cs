using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Writes validation failures into the extensions of a problem response.
/// </summary>
public interface IValidationErrorsWriter
{
    /// <summary>
    ///     Writes validation failures into the extensions of a problem response.
    /// </summary>
    /// <param name="problem">The problem response to extend.</param>
    /// <param name="failures">The validation failures the validator stage collected.</param>
    void WriteErrors(ProblemDetails problem, IReadOnlyList<ValidationFailure> failures);
}
