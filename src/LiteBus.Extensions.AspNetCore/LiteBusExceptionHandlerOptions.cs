namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for mapping LiteBus mediation exceptions to problem responses.
/// </summary>
public sealed class LiteBusExceptionHandlerOptions
{
    /// <summary>
    ///     Gets or sets how a refusal code with no catalog mapping is answered.
    /// </summary>
    /// <value>The default is <see cref="UnmappedRefusalCode.UseFallback" />.</value>
    public UnmappedRefusalCode UnmappedRefusalCode { get; set; } = UnmappedRefusalCode.UseFallback;
}
