namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     One refusal code mapping in the refusal catalog, with the owner that registered it.
/// </summary>
/// <param name="Pattern">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
/// <param name="Kind">The refusal kind transports map to their own status.</param>
/// <param name="Title">The stable short title.</param>
/// <param name="Retryable">Whether retrying the same request can succeed.</param>
/// <param name="Owner">The module or component that registered the mapping, or <see langword="null" /> when unknown.</param>
/// <remarks>
///     <para>
///         Transport-neutral: it names what the refusal means, never how one transport renders it. The ASP.NET Core
///         adapter reads these entries into problem details through
///         <c>RefusalProblemCatalog.MapFrom</c>, and any other transport reads them the same way, so one registration
///         answers on every transport.
///     </para>
///     <para>
///         A fault is never an entry: a configuration or dependency failure ends the mediation as
///         <see cref="MediationOutcome.Failed" /> with a fault code, and transports answer it as a server error
///         without consulting the catalog.
///     </para>
/// </remarks>
public sealed record RefusalEntry(string Pattern, RefusalKind Kind, string Title, bool Retryable, string? Owner);
