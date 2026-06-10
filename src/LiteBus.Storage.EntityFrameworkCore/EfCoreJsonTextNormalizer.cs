using System.Text.Json;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Normalizes JSON text read from relational <c>json</c> or <c>jsonb</c> columns for stable round-trip comparisons.
/// </summary>
internal static class EfCoreJsonTextNormalizer
{
    /// <summary>
    ///     Normalizes JSON text to a compact canonical form.
    /// </summary>
    /// <param name="json">The JSON text returned by the database provider.</param>
    /// <returns>The normalized JSON text, or <see langword="null" /> when the input is empty.</returns>
    internal static string? Normalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
