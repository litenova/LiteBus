using System;

namespace LiteBus.Messaging.Abstractions.Authorization;

/// <summary>
///     The policy result that reports evaluation failed with a configuration or dependency fault.
/// </summary>
/// <param name="Code">The stable fault code operators switch on.</param>
/// <param name="Reason">What failed, written for a person.</param>
/// <param name="Cause">The fault that was caught, or <see langword="null" /> when none was.</param>
/// <remarks>
///     The policy layer maps this to a pipeline fault rather than a denial, so combinators and refusal rewriting
///     pass it through unchanged.
/// </remarks>
public sealed record PolicyFault(string Code, string Reason, Exception? Cause = null) : PolicyResult;
