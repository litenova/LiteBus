using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Declares that the annotated message deliberately states nothing for one metadata type, and records why.
/// </summary>
/// <remarks>
///     <para>
///         Pair it with <c>RequireDeclaration&lt;TValue&gt;</c> on the messaging module. The requirement fails
///         composition for any registered message that neither declares the value nor carries an exemption, which turns
///         a written policy such as "every command states the permission it requires" into a startup failure instead of
///         something code review has to catch.
///     </para>
///     <para>
///         It may be applied more than once, and every instance is aggregated into one
///         <see cref="DeclarationExemptions" /> metadata value.
///     </para>
///     <para>
///         Auditing is the exception: <see cref="AuditExemptAttribute" /> already produces an
///         <see cref="AuditDeclaration" />, so an audit-exempt message satisfies
///         <c>RequireDeclaration&lt;AuditDeclaration&gt;</c> without this attribute. The audit position models both
///         answers in one value type; a requirement over an application's own value type usually cannot.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [DeclarationExempt(typeof(RequiredPermission), "the storefront is public, so there is no actor to authorize")]
/// public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class DeclarationExemptAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DeclarationExemptAttribute" /> class.
    /// </summary>
    /// <param name="declarationType">The metadata value type the message is exempt from declaring.</param>
    /// <param name="rationale">The recorded reason the message is exempt.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declarationType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="rationale" /> is null or whitespace.</exception>
    public DeclarationExemptAttribute(Type declarationType, string rationale)
    {
        ArgumentNullException.ThrowIfNull(declarationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);

        DeclarationType = declarationType;
        Rationale = rationale;
    }

    /// <summary>
    ///     Gets the metadata value type the message is exempt from declaring.
    /// </summary>
    public Type DeclarationType { get; }

    /// <summary>
    ///     Gets the recorded reason the message is exempt.
    /// </summary>
    public string Rationale { get; }

    /// <summary>
    ///     Creates the exemption this attribute declares.
    /// </summary>
    /// <returns>The exemption and its rationale.</returns>
    /// <remarks>
    ///     This attribute does not implement <see cref="IMessageDeclarationSource" />, unlike the declarations it sits
    ///     beside. That contract maps one attribute to one metadata value, and several exemptions have to collapse into
    ///     one set, so the registry aggregates them instead of letting the last one overwrite the rest.
    /// </remarks>
    public DeclarationExemption CreateExemption()
    {
        return new DeclarationExemption(DeclarationType, Rationale);
    }
}
