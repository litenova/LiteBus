using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that the annotated message is deliberately not audited, and records why.
/// </summary>
/// <remarks>
///     <para>
///         An exemption is a decision, not an omission. Recording the rationale beside the message is what makes the
///         selection of audited events reviewable, and is what auditing standards ask for when they require event
///         selection to be documented.
///     </para>
///     <para>
///         Pair with <see cref="AuditedAttribute" />, so a message carrying neither can be reported as undeclared rather
///         than silently going unaudited.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [AuditExempt("browsing a public storefront is not a sensitive action")]
/// public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AuditExemptAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditExemptAttribute" /> class.
    /// </summary>
    /// <param name="rationale">The recorded reason the message is exempt from auditing.</param>
    public AuditExemptAttribute(string rationale)
    {
        Rationale = rationale;
    }

    /// <summary>
    ///     Gets the recorded reason the message is exempt from auditing.
    /// </summary>
    public string Rationale { get; }

    /// <summary>
    ///     Converts the attribute to the declaration stored in message metadata.
    /// </summary>
    /// <returns>The equivalent <see cref="AuditDeclaration" />.</returns>
    public AuditDeclaration ToDeclaration()
    {
        return AuditDeclaration.Exempt(Rationale);
    }
}
