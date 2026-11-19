namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     The refusal codes the tenancy guard and tenant policies report when a mediation has no tenant or a malformed
///     tenant.
/// </summary>
/// <remarks>
///     <para>
///         Carried on the denial refusal, so <c>TrySendAsync</c> returns them and the
///         <see cref="LiteBusMessageDeniedException" /> <c>SendAsync</c> raises exposes them as its
///         <see cref="LiteBusMessageDeniedException.Code" />. An HTTP boundary maps a missing tenant to a forbidden
///         response, because the caller reached a tenant-scoped use case with no tenant to scope it to, and a malformed
///         tenant to an invalid request, because the tenant value itself cannot be used.
///     </para>
/// </remarks>
public static class TenancyRefusalCodes
{
    /// <summary>
    ///     The code reported when a message declaring <see cref="TenantRequiredDeclaration" /> is mediated while the
    ///     execution identity of the mediation in flight has no tenant.
    /// </summary>
    public const string TenantRequired = "tenancy.required";

    /// <summary>
    ///     The code reported when the tenant of the mediation in flight is malformed under the registered
    ///     <see cref="ITenantIdentifierValidator" />.
    /// </summary>
    public const string Invalid = "tenancy.invalid";
}
