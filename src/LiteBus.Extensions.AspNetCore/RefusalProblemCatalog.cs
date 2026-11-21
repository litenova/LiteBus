using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.AspNetCore.Http;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     The default <see cref="IRefusalProblemCatalog" />, carrying the refusal codes LiteBus reports.
/// </summary>
/// <remarks>
///     <para>
///         The default mappings answer a missing declaration, an authorization denial, and a missing tenant with
///         <c>403 Forbidden</c>; a caller that is not signed in with <c>401 Unauthorized</c>; a validation failure
///         with <c>422 Unprocessable Content</c>; a concurrency conflict with <c>409 Conflict</c>; an idempotency key
///         another delivery still holds with <c>409 Conflict</c>; an idempotency key reused for different content with
///         <c>422 Unprocessable Content</c>; and anything else with <c>400 Bad Request</c>.
///     </para>
///     <para>
///         Resolution order is the exact code mapping, then the longest prefix mapping, then the outcome-kind default
///         set by <see cref="MapInvalid" /> or <see cref="MapDenied" />, then the fallback. Both outcome defaults start
///         as the fallback answer, so an unmapped refusal answers <c>400 Bad Request</c> unless the host sets an
///         outcome default or maps the code. An outcome-kind default still reports unmapped from
///         <see cref="TryResolve(string?, MediationOutcome, out RefusalProblemMap)" />, so the per-outcome
///         unmapped-code policy in <see cref="RefusalProblemOptions" /> applies to codes with no explicit mapping.
///     </para>
///     <para>
///         <see cref="Default" /> is shared by the process: map on a catalog built by <see cref="CreateDefault" /> and
///         register that instance in request services instead, so one host cannot change the answers of another.
///     </para>
/// </remarks>
public sealed class RefusalProblemCatalog : IRefusalProblemCatalog
{
    /// <summary>
    ///     The suffix that marks a mapping pattern as a prefix pattern.
    /// </summary>
    private const string PrefixSuffix = ".*";

    /// <summary>
    ///     The status code answered when no mapping matches.
    /// </summary>
    private const int FallbackStatusCode = StatusCodes.Status400BadRequest;

    /// <summary>
    ///     The title answered when no mapping matches.
    /// </summary>
    private const string FallbackTitle = "Invalid request";

    /// <summary>
    ///     The contributor recorded for the mappings LiteBus registers.
    /// </summary>
    private const string LiteBusContributor = "LiteBus";

    /// <summary>
    ///     The registered mappings by exact code or prefix pattern.
    /// </summary>
    private readonly ConcurrentDictionary<string, RefusalProblemEntry> _mappings = new(StringComparer.Ordinal);

    /// <summary>
    ///     The mapping answered for a denied refusal with no code mapping.
    /// </summary>
    private RefusalProblemMap _deniedDefault = new(FallbackStatusCode, FallbackTitle);

    /// <summary>
    ///     The mapping answered for an invalid refusal with no code mapping.
    /// </summary>
    private RefusalProblemMap _invalidDefault = new(FallbackStatusCode, FallbackTitle);

    /// <summary>
    ///     Initializes a new instance of the <see cref="RefusalProblemCatalog" /> class.
    /// </summary>
    public RefusalProblemCatalog()
    {
    }

    /// <summary>
    ///     Gets the shared catalog carrying the default mappings.
    /// </summary>
    public static RefusalProblemCatalog Default { get; } = CreateDefault();

    /// <summary>
    ///     Gets every registered mapping with its contributor, ordered by pattern.
    /// </summary>
    public IReadOnlyList<RefusalProblemEntry> Entries =>
        _mappings.Values.OrderBy(entry => entry.Pattern, StringComparer.Ordinal).ToList();

    /// <summary>
    ///     Creates a catalog carrying the default mappings.
    /// </summary>
    /// <returns>A new catalog the caller may extend with <see cref="Map" /> without affecting other hosts.</returns>
    public static RefusalProblemCatalog CreateDefault()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("declaration.missing", StatusCodes.Status403Forbidden, "Request denied", LiteBusContributor);
        catalog.Map("authorization.not-signed-in", StatusCodes.Status401Unauthorized, "Not signed in", LiteBusContributor);
        catalog.Map("authorization.*", StatusCodes.Status403Forbidden, "Request denied", LiteBusContributor);
        catalog.Map("tenancy.required", StatusCodes.Status403Forbidden, "Request denied", LiteBusContributor);
        catalog.Map("validation.*", StatusCodes.Status422UnprocessableEntity, "Request validation failed", LiteBusContributor);
        catalog.Map("concurrency.conflict", StatusCodes.Status409Conflict, "Concurrency conflict", LiteBusContributor);
        catalog.Map(IdempotencyRefusalCodes.InFlight, StatusCodes.Status409Conflict, "Idempotent request in flight", LiteBusContributor);
        catalog.Map(
            IdempotencyRefusalCodes.FingerprintMismatch,
            StatusCodes.Status422UnprocessableEntity,
            "Idempotency key already used",
            LiteBusContributor);

        return catalog;
    }

    /// <inheritdoc />
    public IRefusalProblemCatalog Map(string code, int statusCode, string title, string? contributor = null)
    {
        ValidateMapping(code, statusCode, title);

        var entry = new RefusalProblemEntry(code, statusCode, title, NormalizeContributor(contributor));

        while (true)
        {
            if (_mappings.TryGetValue(code, out var existing))
            {
                if (existing.StatusCode == statusCode && string.Equals(existing.Title, title, StringComparison.Ordinal))
                {
                    return this;
                }

                throw new LiteBusConfigurationException(
                    $"Refusal code pattern '{code}' is already mapped to {existing.StatusCode} '{existing.Title}'"
                    + DescribeContributor(existing.Contributor)
                    + $" and cannot be remapped to {statusCode} '{title}'"
                    + DescribeContributor(entry.Contributor)
                    + ". Call Replace to override the mapping deliberately.");
            }

            if (_mappings.TryAdd(code, entry))
            {
                return this;
            }
        }
    }

    /// <inheritdoc />
    public IRefusalProblemCatalog Replace(string code, int statusCode, string title, string? contributor = null)
    {
        ValidateMapping(code, statusCode, title);

        _mappings[code] = new RefusalProblemEntry(code, statusCode, title, NormalizeContributor(contributor));

        return this;
    }

    /// <inheritdoc />
    public IRefusalProblemCatalog MapInvalid(int statusCode, string title)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(statusCode, 599);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        _invalidDefault = new RefusalProblemMap(statusCode, title);

        return this;
    }

    /// <inheritdoc />
    public IRefusalProblemCatalog MapDenied(int statusCode, string title)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(statusCode, 599);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        _deniedDefault = new RefusalProblemMap(statusCode, title);

        return this;
    }

    /// <inheritdoc />
    public RefusalProblemMap Resolve(string? code)
    {
        TryResolve(code, out var map);

        return map;
    }

    /// <inheritdoc />
    public RefusalProblemMap Resolve(string? code, MediationOutcome outcome)
    {
        TryResolve(code, outcome, out var map);

        return map;
    }

    /// <inheritdoc />
    public bool TryResolve(string? code, out RefusalProblemMap map)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (_mappings.TryGetValue(code, out var exact))
            {
                map = new RefusalProblemMap(exact.StatusCode, exact.Title);

                return true;
            }

            var match = FindLongestPrefixMatch(code);

            if (match is not null)
            {
                map = new RefusalProblemMap(match.StatusCode, match.Title);

                return true;
            }
        }

        map = new RefusalProblemMap(FallbackStatusCode, FallbackTitle);

        return false;
    }

    /// <inheritdoc />
    public bool TryResolve(string? code, MediationOutcome outcome, out RefusalProblemMap map)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (_mappings.TryGetValue(code, out var exact))
            {
                map = new RefusalProblemMap(exact.StatusCode, exact.Title);

                return true;
            }

            var match = FindLongestPrefixMatch(code);

            if (match is not null)
            {
                map = new RefusalProblemMap(match.StatusCode, match.Title);

                return true;
            }
        }

        map = outcome switch
        {
            MediationOutcome.Invalid => _invalidDefault,
            MediationOutcome.Denied => _deniedDefault,
            _ => new RefusalProblemMap(FallbackStatusCode, FallbackTitle),
        };

        return false;
    }

    /// <summary>
    ///     Exports every registered mapping with its contributor, plus the outcome defaults.
    /// </summary>
    /// <returns>The catalog contents for OpenAPI and documentation tooling.</returns>
    public RefusalProblemCatalogExport Export()
    {
        return new RefusalProblemCatalogExport(Entries, _invalidDefault, _deniedDefault);
    }

    /// <summary>
    ///     Exports every registered mapping with its contributor as JSON for OpenAPI and documentation tooling.
    /// </summary>
    /// <param name="options">The serializer options, or <see langword="null" /> for the web defaults.</param>
    /// <returns>The catalog contents as JSON.</returns>
    public string ExportJson(JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(Export(), options ?? JsonSerializerOptions.Web);
    }

    /// <summary>
    ///     Validates a code mapping before it is registered.
    /// </summary>
    /// <param name="code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    private static void ValidateMapping(string code, int statusCode, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfLessThan(statusCode, 100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(statusCode, 599);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (code.Contains('*') && !IsPrefixPattern(code))
        {
            throw new ArgumentException(
                "A mapping pattern containing '*' must end with '.*' and name a non-empty prefix, such as 'authorization.*'.",
                nameof(code));
        }
    }

    /// <summary>
    ///     Normalizes a contributor name, treating a blank name as unknown.
    /// </summary>
    /// <param name="contributor">The contributor name the caller supplied.</param>
    /// <returns>The contributor name, or <see langword="null" /> when unknown.</returns>
    private static string? NormalizeContributor(string? contributor)
    {
        return string.IsNullOrWhiteSpace(contributor) ? null : contributor;
    }

    /// <summary>
    ///     Describes the origin of a mapping for a conflict message.
    /// </summary>
    /// <param name="contributor">The contributor that registered the mapping, when known.</param>
    /// <returns>The origin clause, or an empty string when the origin is unknown.</returns>
    private static string DescribeContributor(string? contributor)
    {
        return contributor is null ? string.Empty : $" by {contributor}";
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
    private RefusalProblemEntry? FindLongestPrefixMatch(string code)
    {
        RefusalProblemEntry? match = null;
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
