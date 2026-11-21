using System;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares that the annotated message runs across tenants under a stated reason, such as a retention or integrity
///     job, and records why.
/// </summary>
/// <remarks>
///     <para>
///         Running across tenants is a decision, not an omission. Recording the rationale beside the message is what
///         makes the selection of cross-tenant messages reviewable, and is what keeps a missing tenant check from
///         reading as an approved position during a review.
///     </para>
///     <para>
///         The attribute is the lightweight form of a tenancy declaration. When a message needs richer or
///         compile-checked configuration, declare an <c>ITenancyDefinition&lt;TMessage&gt;</c> in a definition class
///         instead. A definition wins when both are present, because both contribute the same
///         <see cref="TenancyDeclaration" /> and the definition is applied last.
///     </para>
///     <para>
///         Pair with <see cref="TenantRequiredAttribute" /> and <see cref="NoTenantAttribute" />, so a message carrying
///         none of the three can be reported as undeclared rather than silently running outside any tenant.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [CrossTenant("the retention job purges expired entries across every workspace")]
/// public sealed record PurgeExpiredEntriesCommand(DateOnly Before) : ICommand;
/// ]]></code>
/// </example>
[MessageDeclaration(typeof(TenancyDeclaration))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CrossTenantAttribute : Attribute, IMessageDeclarationSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CrossTenantAttribute" /> class.
    /// </summary>
    /// <param name="reason">Why the message may act across tenants.</param>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    public CrossTenantAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>
    ///     Gets why the message may act across tenants.
    /// </summary>
    public string Reason { get; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(TenancyDeclaration);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return TenancyDeclaration.CrossTenant(Reason);
    }
}
