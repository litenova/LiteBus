namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for mapping LiteBus mediation exceptions to problem responses.
/// </summary>
/// <remarks>
///     <para>
///         When request services hold a <see cref="RefusalProblemOptions" /> instance, its unmapped-code policies govern
///         the exception path. These policies apply only when none is registered.
///     </para>
/// </remarks>
public sealed class LiteBusExceptionHandlerOptions
{
    /// <summary>
    ///     Gets or sets how a refusal code with no catalog mapping is answered.
    /// </summary>
    /// <value>The default is <see cref="UnmappedRefusalCode.UseFallback" />.</value>
    public UnmappedRefusalCode UnmappedRefusalCode { get; set; } = UnmappedRefusalCode.UseFallback;

    /// <summary>
    ///     Gets or sets how an invalid result's validation code with no catalog mapping is answered.
    /// </summary>
    /// <value>The default is <see cref="UnmappedValidationCode.UseFallback" />.</value>
    public UnmappedValidationCode UnmappedValidationCode { get; set; } = UnmappedValidationCode.UseFallback;
}
