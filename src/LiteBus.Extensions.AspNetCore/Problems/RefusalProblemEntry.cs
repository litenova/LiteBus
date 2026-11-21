namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     One refusal code mapping in a problem catalog, with the contributor that registered it.
/// </summary>
/// <param name="Pattern">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Title">The stable problem title.</param>
/// <param name="Contributor">The module or component that registered the mapping, or <see langword="null" /> when unknown.</param>
public sealed record RefusalProblemEntry(string Pattern, int StatusCode, string Title, string? Contributor);
