namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Decides how the exception handler answers a refusal code with no catalog mapping.
/// </summary>
public enum UnmappedRefusalCode
{
    /// <summary>
    ///     Answer the catalog fallback: <c>400 Bad Request</c> with the refusal detail.
    /// </summary>
    UseFallback = 0,

    /// <summary>
    ///     Answer <c>500 Internal Server Error</c> and log the unmapped code at error level.
    /// </summary>
    TreatAsServerError = 1,
}
