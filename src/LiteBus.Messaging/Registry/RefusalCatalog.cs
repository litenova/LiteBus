using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     The default <see cref="IRefusalCatalog" />, carrying the refusal codes LiteBus reports.
/// </summary>
/// <remarks>
///     <para>
///         The default mappings describe a missing declaration, an authorization denial, and a missing tenant as
///         denied; a caller that is not signed in as denied; a malformed tenant and a validation failure as invalid;
///         a concurrency conflict, an idempotency key another delivery still holds, and a duplicate idempotency key
///         within one batch as conflicts; and an idempotency key reused for different content as invalid. A conflict
///         from a concurrent writer or an in-flight key clears on retry, while a denial, a validation failure, a
///         fingerprint mismatch, and a duplicate key within one batch all answer the same on retry.
///     </para>
///     <para>
///         Resolution order is the exact code mapping, then the longest prefix mapping, then nothing: the catalog
///         reports whether a mapping matches and leaves defaulting to the transport, so there is one defaulting
///         story per transport instead of a neutral default competing with it.
///     </para>
///     <para>
///         Fault codes are never registered here. A fault is carried by <see cref="MessageFaultException" />, ends
///         the mediation as <see cref="MediationOutcome.Failed" />, and is answered as a server error without
///         consulting the catalog.
///     </para>
/// </remarks>
internal sealed class RefusalCatalog : IRefusalCatalog, ICompositionSectionContributor
{
    /// <summary>
    ///     The suffix that marks a mapping pattern as a prefix pattern.
    /// </summary>
    private const string PrefixSuffix = ".*";

    /// <summary>
    ///     The owner recorded for the mappings LiteBus registers.
    /// </summary>
    private const string LiteBusOwner = "LiteBus";

    /// <summary>
    ///     The fault codes LiteBus reports through <see cref="MessageFaultException" />, which the catalog refuses
    ///     to register.
    /// </summary>
    private static readonly HashSet<string> FaultCodes = new(StringComparer.Ordinal)
    {
        AuthorizationPolicyRefusalCodes.UnknownPolicy,
        AuthorizationPolicyRefusalCodes.PermissionLookupMissing,
        AuthorizationPolicyRefusalCodes.ConditionMissing,
        AuthorizationPolicyRefusalCodes.DecisionPointMissing,
        AuthorizationPolicyRefusalCodes.SignInInstantUnknown,
        AuthorizationPolicyRefusalCodes.RecentSignInWindowMissing,
        DomainEventFaultCodes.UndecidedDomainEvent,
        DomainEventFaultCodes.TranslatorMissing,
        DomainEventFaultCodes.InvalidTranslation,
        DomainEventFaultCodes.EventMediatorMissing,
        DomainEventFaultCodes.InProcessPublishUnavailable,
    };

    /// <summary>
    ///     The registered mappings by exact code or prefix pattern.
    /// </summary>
    private readonly ConcurrentDictionary<string, RefusalEntry> _mappings = new(StringComparer.Ordinal);

    /// <summary>
    ///     Initializes a new instance of the <see cref="RefusalCatalog" /> class.
    /// </summary>
    public RefusalCatalog()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<RefusalEntry> Entries =>
        _mappings.Values.OrderBy(entry => entry.Pattern, StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    public string SectionName => "refusals";

    /// <summary>
    ///     Creates a catalog carrying the default mappings.
    /// </summary>
    /// <returns>A new catalog the caller may extend with <see cref="Map" /> without affecting other hosts.</returns>
    public static RefusalCatalog CreateDefault()
    {
        var catalog = new RefusalCatalog();
        catalog.Map("declaration.missing", RefusalKind.Denied, "Request denied", retryable: false, owner: LiteBusOwner);
        catalog.Map(AuthorizationPolicyRefusalCodes.NotSignedIn, RefusalKind.Denied, "Not signed in", retryable: false, owner: LiteBusOwner);
        catalog.Map("authorization.*", RefusalKind.Denied, "Request denied", retryable: false, owner: LiteBusOwner);
        catalog.Map(TenancyRefusalCodes.TenantRequired, RefusalKind.Denied, "Request denied", retryable: false, owner: LiteBusOwner);
        catalog.Map(TenancyRefusalCodes.Invalid, RefusalKind.Invalid, "Invalid tenant", retryable: false, owner: LiteBusOwner);
        catalog.Map("validation.*", RefusalKind.Invalid, "Request validation failed", retryable: false, owner: LiteBusOwner);
        catalog.Map(ConcurrencyRefusalCodes.Conflict, RefusalKind.Conflict, "Concurrency conflict", retryable: true, owner: LiteBusOwner);
        catalog.Map(IdempotencyRefusalCodes.InFlight, RefusalKind.Conflict, "Idempotent request in flight", retryable: true, owner: LiteBusOwner);
        catalog.Map(
            IdempotencyRefusalCodes.FingerprintMismatch,
            RefusalKind.Invalid,
            "Idempotency key already used",
            retryable: false,
            owner: LiteBusOwner);
        catalog.Map(
            IdempotencyRefusalCodes.DuplicateKeyInBatch,
            RefusalKind.Conflict,
            "Duplicate idempotency key in batch",
            retryable: false,
            owner: LiteBusOwner);

        return catalog;
    }

    /// <inheritdoc />
    public IRefusalWriter Map(string code, RefusalKind kind, string title, bool retryable, string? owner = null)
    {
        ValidateMapping(code, kind, title);
        ThrowIfFaultCode(code);

        var entry = new RefusalEntry(code, kind, title, retryable, NormalizeOwner(owner));

        while (true)
        {
            if (_mappings.TryGetValue(code, out var existing))
            {
                if (existing.Kind.Equals(kind)
                    && string.Equals(existing.Title, title, StringComparison.Ordinal)
                    && existing.Retryable == retryable)
                {
                    return this;
                }

                throw new LiteBusConfigurationException(
                    $"Refusal code pattern '{code}' is already mapped to {DescribeMapping(existing)}"
                    + DescribeOwner(existing.Owner)
                    + $" and cannot be remapped to {DescribeMapping(entry)}"
                    + DescribeOwner(entry.Owner)
                    + ". Call Replace to override the mapping deliberately.");
            }

            if (_mappings.TryAdd(code, entry))
            {
                return this;
            }
        }
    }

    /// <inheritdoc />
    public IRefusalWriter Replace(string code, RefusalKind kind, string title, bool retryable, string? owner = null)
    {
        ValidateMapping(code, kind, title);
        ThrowIfFaultCode(code);

        _mappings[code] = new RefusalEntry(code, kind, title, retryable, NormalizeOwner(owner));

        return this;
    }

    /// <summary>
    ///     Harvests the refusal declarations of every concrete message into the catalog.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="catalog">The catalog receiving the harvested entries.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="reader" /> or <paramref name="catalog" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     Runs as a composition validation, after conventions are applied and before the composition checks
    ///     build their model, so convention-supplied declarations are harvested and the <c>refusals</c>
    ///     composition section reports them. Each entry is owned by the definition or attribute type that
    ///     declared it, or by the scope a default or convention declared it for. Abstract types and interfaces
    ///     are skipped: they are shapes rather than messages, and their declarations reach the catalog through
    ///     the concrete messages beneath them.
    /// </remarks>
    internal static void HarvestDeclarations(IMessageReader reader, IRefusalWriter catalog)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(catalog);

        foreach (var descriptor in reader)
        {
            if (descriptor.MessageType.IsAbstract || descriptor.MessageType.IsInterface)
            {
                continue;
            }

            if (!descriptor.Metadata.TryGet<RefusalDeclarations>(out var declared) || declared is null)
            {
                continue;
            }

            var owner = DescribeSource(descriptor);

            foreach (var refusal in declared.Values)
            {
                catalog.Map(refusal.Code, refusal.Kind, refusal.Title, refusal.Retryable, owner);
            }
        }
    }

    /// <inheritdoc />
    public bool TryResolve(string? code, [NotNullWhen(true)] out RefusalEntry? entry)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (_mappings.TryGetValue(code, out var exact))
            {
                entry = exact;

                return true;
            }

            var match = FindLongestPrefixMatch(code);

            if (match is not null)
            {
                entry = match;

                return true;
            }
        }

        entry = null;

        return false;
    }

    /// <inheritdoc />
    public object? GetData()
    {
        var entries = Entries;

        if (entries.Count == 0)
        {
            return null;
        }

        List<SortedDictionary<string, object?>> mappings = new(entries.Count);

        foreach (var entry in entries)
        {
            mappings.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pattern"] = entry.Pattern,
                ["kind"] = KindName(entry.Kind),
                ["title"] = entry.Title,
                ["retryable"] = entry.Retryable,
                ["owner"] = entry.Owner,
            });
        }

        return new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["entries"] = mappings,
        };
    }

    /// <summary>
    ///     Validates a code mapping before it is registered.
    /// </summary>
    /// <param name="code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
    /// <param name="kind">The refusal kind transports map to their own status.</param>
    /// <param name="title">The stable short title.</param>
    private static void ValidateMapping(string code, RefusalKind kind, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (code.Contains('*') && !IsPrefixPattern(code))
        {
            throw new ArgumentException(
                "A mapping pattern containing '*' must end with '.*' and name a non-empty prefix, such as 'authorization.*'.",
                nameof(code));
        }
    }

    /// <summary>
    ///     Normalizes an owner name, treating a blank name as unknown.
    /// </summary>
    /// <param name="owner">The owner name the caller supplied.</param>
    /// <returns>The owner name, or <see langword="null" /> when unknown.</returns>
    private static string? NormalizeOwner(string? owner)
    {
        return string.IsNullOrWhiteSpace(owner) ? null : owner;
    }

    /// <summary>
    ///     Rejects a code LiteBus reports as a fault rather than a refusal.
    /// </summary>
    /// <param name="code">The refusal code being registered.</param>
    /// <exception cref="LiteBusConfigurationException">The code is a known fault code.</exception>
    /// <remarks>
    ///     Only exact fault codes are rejected: a prefix pattern such as <c>authorization.*</c> legitimately
    ///     covers denials, and faults never consult the catalog, so the overlap is harmless.
    /// </remarks>
    private static void ThrowIfFaultCode(string code)
    {
        if (FaultCodes.Contains(code))
        {
            throw new LiteBusConfigurationException(
                $"Refusal code '{code}' is a fault LiteBus reports through '{nameof(MessageFaultException)}', "
                + "so it cannot be registered in the refusal catalog. A configuration fault is never a refusal: "
                + "it ends the mediation as Failed, and transports answer it as a server error.");
        }
    }

    /// <summary>
    ///     Names the source that declared a message's refusals, for catalog ownership.
    /// </summary>
    /// <param name="descriptor">The message descriptor holding the declarations.</param>
    /// <returns>
    ///     The definition or attribute type name when one declared the value, the scope a default or convention
    ///     declared it for otherwise, or the message type name when provenance is unavailable.
    /// </returns>
    private static string DescribeSource(IMessageDescriptor descriptor)
    {
        if (descriptor.Metadata is MessageMetadata metadata)
        {
            foreach (var provenance in metadata.Provenance)
            {
                if (provenance.KeyType == typeof(RefusalDeclarations))
                {
                    return provenance.SourceType?.Name ?? provenance.DeclaredFor.Name;
                }
            }
        }

        return descriptor.MessageType.Name;
    }

    /// <summary>
    ///     Describes a mapping for a conflict message.
    /// </summary>
    /// <param name="entry">The mapping to describe.</param>
    /// <returns>The kind, title, and retryability of the mapping.</returns>
    private static string DescribeMapping(RefusalEntry entry)
    {
        return entry.Retryable
            ? $"{KindName(entry.Kind)} '{entry.Title}', retryable"
            : $"{KindName(entry.Kind)} '{entry.Title}', not retryable";
    }

    /// <summary>
    ///     Describes the origin of a mapping for a conflict message.
    /// </summary>
    /// <param name="owner">The owner that registered the mapping, when known.</param>
    /// <returns>The origin clause, or an empty string when the origin is unknown.</returns>
    private static string DescribeOwner(string? owner)
    {
        return owner is null ? string.Empty : $" by {owner}";
    }

    /// <summary>
    ///     Names a refusal kind for conflict messages and the composition export.
    /// </summary>
    /// <param name="kind">The refusal kind.</param>
    /// <returns>The kind name, such as <c>Denied</c>.</returns>
    private static string KindName(RefusalKind kind)
    {
        return kind switch
        {
            RefusalKind.DeniedKind => nameof(RefusalKind.Denied),
            RefusalKind.InvalidKind => nameof(RefusalKind.Invalid),
            RefusalKind.ConflictKind => nameof(RefusalKind.Conflict),
            RefusalKind.NotFoundKind => nameof(RefusalKind.NotFound),
            RefusalKind.UnavailableKind => nameof(RefusalKind.Unavailable),
            _ => kind.GetType().Name,
        };
    }

    /// <summary>
    ///     Determines whether a mapping pattern covers a prefix rather than one exact code.
    /// </summary>
    /// <param name="code">The mapping pattern.</param>
    /// <returns><see langword="true" /> when the pattern ends in <c>.*</c> with a non-empty prefix.</returns>
    private static bool IsPrefixPattern(string code)
    {
        return code.EndsWith(PrefixSuffix, StringComparison.Ordinal) && code.Length > PrefixSuffix.Length;
    }

    /// <summary>
    ///     Finds the longest prefix mapping that covers a refusal code.
    /// </summary>
    /// <param name="code">The refusal code.</param>
    /// <returns>The longest matching mapping, or <see langword="null" /> when no prefix covers the code.</returns>
    private RefusalEntry? FindLongestPrefixMatch(string code)
    {
        RefusalEntry? match = null;
        var longestPrefix = -1;

        foreach (var (pattern, mapping) in _mappings)
        {
            if (!IsPrefixPattern(pattern))
            {
                continue;
            }

            var prefix = pattern.Substring(0, pattern.Length - PrefixSuffix.Length);

            if (prefix.Length > longestPrefix
                && code.Length > prefix.Length
                && code[prefix.Length] == '.'
                && code.StartsWith(prefix, StringComparison.Ordinal))
            {
                longestPrefix = prefix.Length;
                match = mapping;
            }
        }

        return match;
    }
}
