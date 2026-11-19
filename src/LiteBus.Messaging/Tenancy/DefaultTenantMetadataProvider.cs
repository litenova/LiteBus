using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Tenancy;

namespace LiteBus.Messaging.Tenancy;

/// <summary>
///     Answers every tenant with unknown metadata, so tenant-aware features run before the application exposes its
///     tenant records.
/// </summary>
/// <remarks>
///     <para>
///         Registered as the <see cref="ITenantMetadataProvider" /> LiteBus default when <c>UseTenancy</c> builds.
///         Every requested tenant receives a <see cref="TenantMetadata" /> carrying its identifier with no time zone,
///         no culture, and no attributes. Schedules and policies read an unknown zone as UTC, and the
///         <see cref="TenantMetadataDiagnosticCheck" /> probe reports that the default answers.
///     </para>
///     <para>
///         An application with tenant records replaces this by registering its own
///         <see cref="ITenantMetadataProvider" /> with the container, and the composition model records the
///         replacement.
///     </para>
/// </remarks>
public sealed class DefaultTenantMetadataProvider : ITenantMetadataProvider
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
        IReadOnlyList<string> tenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        Dictionary<string, TenantMetadata> result = new(StringComparer.Ordinal);

        foreach (var tenantId in tenantIds)
        {
            Dictionary<string, string> attributes = [];

            result[tenantId] = new TenantMetadata(tenantId, null, null, attributes);
        }

        return new ValueTask<IReadOnlyDictionary<string, TenantMetadata>>(result);
    }
}
