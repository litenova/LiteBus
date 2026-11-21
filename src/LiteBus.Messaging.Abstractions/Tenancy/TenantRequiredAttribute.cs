using System;

namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares that mediating the annotated message requires a tenant in its execution identity.
/// </summary>
/// <remarks>
///     <para>
///         The attribute is the lightweight form of a tenancy declaration, for messages whose configuration is a single
///         fact. When a message needs richer or compile-checked configuration, declare an
///         <c>ITenancyDefinition&lt;TMessage&gt;</c> in a definition class instead. A definition wins when both are
///         present, because both contribute the same <see cref="TenancyDeclaration" /> and the definition is applied
///         last.
///     </para>
///     <para>
///         Pair with <see cref="CrossTenantAttribute" /> and <see cref="NoTenantAttribute" /> so that every message
///         states its position explicitly, rather than a message running outside any tenant being indistinguishable
///         from one nobody considered.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// [TenantRequired]
/// public sealed record PostManualEntryCommand(Guid WorkspaceId, EntryDraft Draft) : ICommand;
/// ]]></code>
/// </example>
[MessageDeclaration(typeof(TenancyDeclaration))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class TenantRequiredAttribute : Attribute, IMessageDeclarationSource
{
    /// <inheritdoc />
    public Type DeclarationType => typeof(TenancyDeclaration);

    /// <inheritdoc />
    public object CreateDeclaration()
    {
        return TenancyDeclaration.Required();
    }
}
