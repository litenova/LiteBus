using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Builds the hash scheme new audit records are written with when the host starts, so a missing scheme piece fails
///     startup instead of every link attempt.
/// </summary>
/// <remarks>
///     <para>
///         A shipped scheme resolves its canonicalizer, link rule and hasher from the container. When one of them is
///         missing, resolution fails, and without this check the failure surfaces only when the linker first runs,
///         after records have already been committed unlinked. Running the resolution at startup turns that into a
///         configuration fault that names the missing piece before the first message arrives.
///     </para>
///     <para>
///         The check runs in its own dispatch scope, because scheme pieces may be registered scoped, and it constructs
///         nothing it keeps.
///     </para>
/// </remarks>
internal sealed class AuditHashSchemeStartupCheck : IStartupTask
{
    /// <summary>
    ///     The registry method that builds a scheme for one document type, closed per check.
    /// </summary>
    private static readonly MethodInfo ResolveMethod =
        typeof(IAuditHashSchemeRegistry).GetMethod(nameof(IAuditHashSchemeRegistry.Resolve))
        ?? throw new InvalidOperationException("IAuditHashSchemeRegistry.Resolve was not found.");

    /// <summary>
    ///     Gets the root service provider the check opens its scope from.
    /// </summary>
    private readonly IServiceProvider _services;

    /// <summary>
    ///     Gets the document type the chain stores.
    /// </summary>
    private readonly Type _documentType;

    /// <summary>
    ///     Gets the name of the scheme new records are written with.
    /// </summary>
    private readonly string _schemeName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditHashSchemeStartupCheck" /> class.
    /// </summary>
    /// <param name="services">The root service provider.</param>
    /// <param name="documentType">The document type the chain stores.</param>
    /// <param name="schemeName">The name of the scheme new records are written with.</param>
    public AuditHashSchemeStartupCheck(IServiceProvider services, Type documentType, string schemeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);

        _services = services;
        _documentType = documentType;
        _schemeName = schemeName;
    }

    /// <summary>
    ///     Resolves the write scheme for the stored document and fails startup when it cannot be built.
    /// </summary>
    /// <param name="cancellationToken">The token that stops startup.</param>
    /// <returns>A completed task when the scheme builds.</returns>
    /// <exception cref="AuditConfigurationException">Thrown when the scheme cannot be built from the container.</exception>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var scope = _services.GetService(typeof(IMessageDispatchScopeFactory)) is IMessageDispatchScopeFactory factory
            ? factory.CreateScope()
            : null;

        var provider = scope?.ServiceProvider ?? _services;

        if (provider.GetService(typeof(IAuditHashSchemeRegistry)) is not IAuditHashSchemeRegistry registry)
        {
            throw new AuditConfigurationException(
                "The audit hash chain is enabled but no hash scheme registry is registered, so no record can be linked.");
        }

        try
        {
            ResolveMethod.MakeGenericMethod(_documentType).Invoke(registry, [_schemeName]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new AuditConfigurationException(
                $"The audit hash scheme '{_schemeName}' for '{_documentType.Name}' cannot be built, so no audit record " +
                $"can be linked: {exception.InnerException.Message}",
                exception.InnerException);
        }

        return Task.CompletedTask;
    }
}
