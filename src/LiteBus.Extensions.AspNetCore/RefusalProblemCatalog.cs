using System;
using System.Collections.Concurrent;
using LiteBus.Messaging.Abstractions;
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
    ///     The registered mappings by exact code or prefix pattern.
    /// </summary>
    private readonly ConcurrentDictionary<string, RefusalProblemMap> _mappings = new(StringComparer.Ordinal);

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
    ///     Creates a catalog carrying the default mappings.
    /// </summary>
    /// <returns>A new catalog the caller may extend with <see cref="Map" /> without affecting other hosts.</returns>
    public static RefusalProblemCatalog CreateDefault()
    {
        var catalog = new RefusalProblemCatalog();
        catalog.Map("declaration.missing", StatusCodes.Status403Forbidden, "Request denied");
        catalog.Map("authorization.not-signed-in", StatusCodes.Status401Unauthorized, "Not signed in");
        catalog.Map("authorization.*", StatusCodes.Status403Forbidden, "Request denied");
        catalog.Map("tenancy.required", StatusCodes.Status403Forbidden, "Request denied");
        catalog.Map("validation.*", StatusCodes.Status422UnprocessableEntity, "Request validation failed");
        catalog.Map("concurrency.conflict", StatusCodes.Status409Conflict, "Concurrency conflict");
        catalog.Map(IdempotencyRefusalCodes.InFlight, StatusCodes.Status409Conflict, "Idempotent request in flight");
        catalog.Map(
            IdempotencyRefusalCodes.FingerprintMismatch,
            StatusCodes.Status422UnprocessableEntity,
            "Idempotency key already used");

        return catalog;
    }

    /// <inheritdoc />
    public IRefusalProblemCatalog Map(string code, int statusCode, string title)
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

        _mappings[code] = new RefusalProblemMap(statusCode, title);

        return this;
    }

    /// <inheritdoc />
    public RefusalProblemMap Resolve(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (_mappings.TryGetValue(code, out var exact))
            {
                return exact;
            }

            var match = FindLongestPrefixMatch(code);

            if (match is not null)
            {
                return match;
            }
        }

        return new RefusalProblemMap(FallbackStatusCode, FallbackTitle);
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
    private RefusalProblemMap? FindLongestPrefixMatch(string code)
    {
        RefusalProblemMap? match = null;
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
