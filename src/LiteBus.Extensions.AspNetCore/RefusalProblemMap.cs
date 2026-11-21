namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     The HTTP status code and problem title one refusal code maps to.
/// </summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Title">The stable problem title.</param>
public sealed record RefusalProblemMap(int StatusCode, string Title);
