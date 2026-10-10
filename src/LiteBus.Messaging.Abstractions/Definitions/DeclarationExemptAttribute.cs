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
///         It covers auditing like anything else: <c>[DeclarationExempt(typeof(AuditDeclaration), "why")]</c> exempts a
///         message from producing an audit record and satisfies <c>RequireDeclaration&lt;AuditDeclaration&gt;</c>.
///         <see cref="AuditExemptAttribute" /> is the shorthand for exactly that, and records the same exemption here,
///         so there is one place to read every exemption a message carries whichever spelling wrote it.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [DeclarationExempt(typeof(RequiredPermission), "the storefront is public, so there is no actor to authorize")]
/// public sealed record GetStorefrontQuery(Guid StoreId) : IQuery<StorefrontView>;
/// ]]></code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class DeclarationExemptAttribute : Attribute, IMessageDeclarationExemptionSource
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

    /// <inheritdoc />
    /// <remarks>
    ///     This attribute implements <see cref="IMessageDeclarationExemptionSource" /> rather than
    ///     <see cref="IMessageDeclarationSource" />, unlike the declarations it sits beside. That contract maps one
    ///     attribute to one metadata value and the last one wins, and several exemptions have to collapse into one
    ///     set, so the registry aggregates them instead.
    /// </remarks>
    public DeclarationExemption CreateExemption()
    {
        return new DeclarationExemption(DeclarationType, Rationale);
    }
}
