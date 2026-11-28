using System;
using System.Collections.Generic;

namespace LiteBus.Runtime.Extensions.Microsoft.Hosting;

/// <summary>
///     The outcome of a LiteBus host validation run.
/// </summary>
/// <param name="Failures">One entry for every problem found during the validation run.</param>
/// <param name="SkippedRoundTrips">
///     One entry for every contract whose round trip did not run because no sample was available while
///     <see cref="LiteBusHostValidationOptions.FailOnMissingSample" /> is <see langword="false" />.
/// </param>
/// <remarks>
///     <para>
///         Answered by <see cref="LiteBusHostValidationExtensions.ValidateLiteBusHostWithResult(IServiceProvider, LiteBusHostValidationOptions)" />. A skipped
///         round trip is not a failure: the contract was never serialized, so nothing about its serializer
///         coverage is known. Each entry carries the same diagnosis a failure would have carried, so a host
///         migrating toward full coverage tracks its remaining gaps without failing validation.
///     </para>
/// </remarks>
public sealed record LiteBusHostValidationResult(
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> SkippedRoundTrips);
