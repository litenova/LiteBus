namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Decides how invalid refusal rendering answers a validation code with no catalog mapping.
/// </summary>
/// <remarks>
///     <para>
///         The policy applies on both the result path in <see cref="MediationResultHttpExtensions" /> and the exception
///         path in <see cref="LiteBusExceptionHandler" />, so the two answers cannot drift. Denied refusals follow
///         <see cref="UnmappedRefusalCode" /> instead, so a host stays strict about denials while answering
///         per-validator codes with the invalid default.
///     </para>
/// </remarks>
public enum UnmappedValidationCode
{
    /// <summary>
    ///     Answer the invalid default with the refusal detail.
    /// </summary>
    UseFallback = 0,

    /// <summary>
    ///     Answer <c>500 Internal Server Error</c> with a generic server-error type, and log the unmapped code at
    ///     error level.
    /// </summary>
    TreatAsServerError = 1,
}
