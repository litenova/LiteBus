namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for rendering mediation refusals as problem responses.
/// </summary>
/// <remarks>
///     <para>
///         Both the result path in <see cref="MediationResultHttpExtensions" /> and the exception path in
///         <see cref="LiteBusExceptionHandler" /> read these options from request services, falling back to a default
///         instance when none is registered, so the two answers cannot drift.
///     </para>
/// </remarks>
public sealed class RefusalProblemOptions
{
    /// <summary>
    ///     Gets or sets the formatter for the problem <c>type</c> URI.
    /// </summary>
    /// <value>The default is <see cref="UrnProblemTypeFormatter.Default" />.</value>
    public IProblemTypeFormatter TypeFormatter { get; set; } = UrnProblemTypeFormatter.Default;

    /// <summary>
    ///     Gets or sets the writer for the validation errors extension.
    /// </summary>
    /// <value>The default is <see cref="DictionaryValidationErrorsWriter.Default" />.</value>
    public IValidationErrorsWriter ValidationErrors { get; set; } = DictionaryValidationErrorsWriter.Default;

    /// <summary>
    ///     Gets or sets the enricher for application-specific problem content.
    /// </summary>
    /// <value>The default is <see cref="NullProblemDetailsEnricher.Default" />.</value>
    public IProblemDetailsEnricher Enricher { get; set; } = NullProblemDetailsEnricher.Default;

    /// <summary>
    ///     Gets or sets how a refusal code with no catalog mapping is answered.
    /// </summary>
    /// <value>The default is <see cref="UnmappedRefusalCode.UseFallback" />.</value>
    public UnmappedRefusalCode UnmappedRefusalCode { get; set; } = UnmappedRefusalCode.UseFallback;

    /// <summary>
    ///     Gets or sets a value indicating whether the refusal code is written as the <c>code</c> extension.
    /// </summary>
    /// <value>The default is <see langword="true" />.</value>
    public bool IncludeRefusalCode { get; set; } = true;
}
