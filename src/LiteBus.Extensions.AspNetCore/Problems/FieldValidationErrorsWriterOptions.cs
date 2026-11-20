namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Options for the member names <see cref="FieldValidationErrorsWriter" /> writes.
/// </summary>
/// <remarks>
///     <para>
///         The writer carries one object per validation failure under <see cref="ExtensionName" />, with each object
///         holding the failure member, code, and message under <see cref="FieldMemberName" />,
///         <see cref="CodeMemberName" />, and <see cref="MessageMemberName" />. The three entry member names must
///         differ from each other.
///     </para>
/// </remarks>
public sealed class FieldValidationErrorsWriterOptions
{
    /// <summary>
    ///     Gets or sets the problem extension that carries the per-field entries.
    /// </summary>
    /// <value>The default is <c>fields</c>.</value>
    public string ExtensionName { get; set; } = "fields";

    /// <summary>
    ///     Gets or sets the entry member that carries the failure member.
    /// </summary>
    /// <value>The default is <c>field</c>.</value>
    public string FieldMemberName { get; set; } = "field";

    /// <summary>
    ///     Gets or sets the entry member that carries the failure code.
    /// </summary>
    /// <value>The default is <c>code</c>.</value>
    public string CodeMemberName { get; set; } = "code";

    /// <summary>
    ///     Gets or sets the entry member that carries the failure message.
    /// </summary>
    /// <value>The default is <c>message</c>.</value>
    public string MessageMemberName { get; set; } = "message";
}
