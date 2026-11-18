namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     The refusal codes the tenancy guard reports when a message requiring a tenant is mediated without one.
/// </summary>
/// <remarks>
///     <para>
///         Carried on the denial refusal, so <c>TrySendAsync</c> returns it and the
///         <see cref="LiteBusMessageDeniedException" /> <c>SendAsync</c> raises exposes it as its
///         <see cref="LiteBusMessageDeniedException.Code" />. An HTTP boundary maps it to a forbidden response: the
///         caller reached a tenant-scoped use case with no tenant to scope it to.
///     </para>
/// </remarks>
public static class TenancyRefusalCodes
{
    /// <summary>
    ///     The code reported when a message declaring <see cref="TenantRequiredDeclaration" /> is mediated while the
    ///     execution identity of the mediation in flight has no tenant.
    /// </summary>
    public const string TenantRequired = "tenancy.required";
}
