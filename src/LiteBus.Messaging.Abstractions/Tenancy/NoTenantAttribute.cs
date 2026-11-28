using System;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares that the annotated message belongs to no tenant, such as sign-in, and records why.
/// </summary>
/// <remarks>
///     <para>
///         Having no tenant is a decision, not an omission. Recording the rationale beside the message is what makes
///         the selection of tenantless messages reviewable, and is what keeps a missing tenant check from reading as
///         an approved position during a review.
///     </para>
///     <para>
///         The attribute is the lightweight form of a tenancy declaration. When a message needs richer or
///         compile-checked configuration, declare an <c>ITenancyDefinition&lt;TMessage&gt;</c> in a definition class
///         instead. A definition wins when both are present, because both contribute the same
///         <see cref="TenancyDeclaration" /> and the definition is applied last.
///     </para>
///     <para>
///         Pair with <see cref="TenantRequiredAttribute" /> and <see cref="CrossTenantAttribute" />, so a message
///         carrying none of the three can be reported as undeclared rather than silently running outside any tenant.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [NoTenant("sign-in creates the session, so there is no tenant yet")]
/// public sealed record SignInCommand(string Email, string Password) : ICommand<SessionToken>;
/// ]]></code>
/// </example>
[MessageDeclaration(typeof(TenancyDeclaration))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class NoTenantAttribute : Attribute, IMessageDeclarationSource
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NoTenantAttribute" /> class.
    /// </summary>
    /// <param name="reason">Why the message has no tenant.</param>
    /// <exception cref="ArgumentException"><paramref name="reason" /> is null, empty, or whitespace.</exception>
    public NoTenantAttribute(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>
    ///     Gets why the message has no tenant.
    /// </summary>
    public string Reason { get; }

    /// <inheritdoc />
    public Type DeclarationType => typeof(TenancyDeclaration);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return TenancyDeclaration.None(Reason);
    }
}
