namespace LiteBus.Messaging.Abstractions.Tenancy;

/// <summary>
///     Declares whether a message runs inside a tenant, across tenants for a stated reason, or outside any tenant.
/// </summary>
/// <remarks>
///     <para>
///         Tenancy is a pipeline position like authorization and audit: every message of a multi-tenant application
///         either requires a tenant, runs across tenants under a stated reason (a retention or integrity job), or has
///         none (sign-in). Declare it on a definition class through <see cref="ITenancyDefinition{TMessage}" /> or with
///         the tenancy attributes, and require it at composition with <c>RequireDeclaration&lt;TenancyDeclaration&gt;()</c>.
///     </para>
///     <para>
///         The hierarchy is closed to the three positions LiteBus defines, so the constructor is not accessible outside
///         this assembly.
///     </para>
/// </remarks>
public abstract record TenancyDeclaration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TenancyDeclaration" /> class.
    /// </summary>
    internal TenancyDeclaration()
    {
    }

    /// <summary>
    ///     Gets a value indicating whether the message must run with a tenant in its execution identity.
    /// </summary>
    public abstract bool RequiresTenant { get; }

    /// <summary>
    ///     Creates a declaration for a message that must run inside a tenant.
    /// </summary>
    /// <returns>The tenant-required declaration.</returns>
    public static TenantRequiredDeclaration Required()
    {
        return TenantRequiredDeclaration.Instance;
    }

    /// <summary>
    ///     Creates a declaration for a message that runs across tenants, such as a retention or integrity job.
    /// </summary>
    /// <param name="reason">Why the message may act across tenants.</param>
    /// <returns>The cross-tenant declaration.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static CrossTenantDeclaration CrossTenant(string reason)
    {
        return new CrossTenantDeclaration(reason);
    }

    /// <summary>
    ///     Creates a declaration for a message that belongs to no tenant, such as sign-in.
    /// </summary>
    /// <param name="reason">Why the message has no tenant.</param>
    /// <returns>The no-tenant declaration.</returns>
    /// <exception cref="System.ArgumentException">Thrown when <paramref name="reason" /> is null or whitespace.</exception>
    public static NoTenantDeclaration None(string reason)
    {
        return new NoTenantDeclaration(reason);
    }
}
