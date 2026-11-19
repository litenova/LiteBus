namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Decides how refusal rendering answers a refusal code with no catalog mapping.
/// </summary>
/// <remarks>
///     <para>
///         The policy applies on both the result path in <see cref="MediationResultHttpExtensions" /> and the exception
///         path in <see cref="LiteBusExceptionHandler" />, so the two answers cannot drift.
///     </para>
/// </remarks>
public enum UnmappedRefusalCode
{
    /// <summary>
    ///     Answer the outcome default with the refusal detail.
    /// </summary>
    UseFallback = 0,

    /// <summary>
    ///     Answer <c>500 Internal Server Error</c> with a generic server-error type, and log the unmapped code at
    ///     error level.
    /// </summary>
    TreatAsServerError = 1,
}
