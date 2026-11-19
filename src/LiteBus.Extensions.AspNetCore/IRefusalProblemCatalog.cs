using LiteBus.Messaging.Abstractions;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Maps mediation refusal codes to HTTP status codes and problem titles.
/// </summary>
/// <remarks>
///     <para>
///         An application contributes its own codes with <see cref="Map" /> and registers the catalog in request
///         services, so <see cref="MediationResultHttpExtensions" /> answers product-specific refusals with the status
///         the product chooses. A code ending in <c>.*</c> is a prefix pattern that covers every code under that
///         prefix, such as <c>authorization.*</c> covering <c>authorization.forbidden</c>.
///     </para>
///     <para>
///         Resolution order is the exact code mapping, then the longest prefix mapping, then the outcome-kind default
///         set by <see cref="MapInvalid" /> or <see cref="MapDenied" />, then the fallback. An invalid refusal therefore
///         answers the invalid default whatever the number of validation failures, unless a code mapping is more
///         specific.
///     </para>
///     <para>
///         Registrations follow one conflict rule: mapping a pattern that already carries the same status code and
///         title is a no-op, mapping it to a different status code or title throws, and a deliberate override calls
///         <see cref="Replace" />.
///     </para>
/// </remarks>
public interface IRefusalProblemCatalog
{
    /// <summary>
    ///     Maps a refusal code to an HTTP status code and problem title.
    /// </summary>
    /// <param name="code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    /// <param name="contributor">The module or component registering the mapping, recorded for the catalog export.</param>
    /// <returns>The catalog for chaining.</returns>
    IRefusalProblemCatalog Map(string code, int statusCode, string title, string? contributor = null);

    /// <summary>
    ///     Maps a refusal code to an HTTP status code and problem title, replacing any mapping the pattern carries.
    /// </summary>
    /// <param name="code">The exact refusal code, or a prefix pattern ending in <c>.*</c>.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    /// <param name="contributor">The module or component registering the mapping, recorded for the catalog export.</param>
    /// <returns>The catalog for chaining.</returns>
    IRefusalProblemCatalog Replace(string code, int statusCode, string title, string? contributor = null);

    /// <summary>
    ///     Sets the mapping answered for an invalid refusal with no code mapping.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    /// <returns>The catalog for chaining.</returns>
    IRefusalProblemCatalog MapInvalid(int statusCode, string title);

    /// <summary>
    ///     Sets the mapping answered for a denied refusal with no code mapping.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The stable problem title.</param>
    /// <returns>The catalog for chaining.</returns>
    IRefusalProblemCatalog MapDenied(int statusCode, string title);

    /// <summary>
    ///     Resolves the mapping for a refusal code.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <returns>The matching mapping, or the fallback mapping when nothing matches.</returns>
    RefusalProblemMap Resolve(string? code);

    /// <summary>
    ///     Resolves the mapping for a refusal code with its mediation outcome.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <returns>
    ///     The exact mapping, the longest prefix mapping, the outcome-kind default, or the fallback mapping when
    ///     nothing matches.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         The default implementation answers through the code-only overload, so a catalog that maps by code only
    ///         keeps compiling and answers without an outcome default.
    ///     </para>
    /// </remarks>
    RefusalProblemMap Resolve(string? code, MediationOutcome outcome)
    {
        return Resolve(code);
    }

    /// <summary>
    ///     Tries to resolve the mapping for a refusal code, reporting whether a mapping matches.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <param name="map">The matching mapping, or the fallback mapping when nothing matches.</param>
    /// <returns><see langword="true" /> when a mapping matches; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     <para>
    ///         The default implementation resolves the mapping and returns <see langword="false" />, so a catalog that
    ///         does not report matches fails fast under an unmapped-code policy instead of silently reporting every
    ///         code as mapped.
    ///     </para>
    /// </remarks>
    bool TryResolve(string? code, out RefusalProblemMap map)
    {
        map = Resolve(code);

        return false;
    }

    /// <summary>
    ///     Tries to resolve the mapping for a refusal code with its mediation outcome, reporting whether a code
    ///     mapping matches.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <param name="outcome">How the mediation ended.</param>
    /// <param name="map">The matching mapping, the outcome-kind default, or the fallback mapping.</param>
    /// <returns>
    ///     <see langword="true" /> when an exact or prefix mapping matches; otherwise, <see langword="false" />. An
    ///     outcome-kind default still reports <see langword="false" />, so the unmapped-code policy applies to codes
    ///     with no explicit mapping.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         The default implementation resolves the mapping and returns <see langword="false" />, so a catalog that
    ///         does not report matches fails fast under an unmapped-code policy instead of silently reporting every
    ///         code as mapped.
    ///     </para>
    /// </remarks>
    bool TryResolve(string? code, MediationOutcome outcome, out RefusalProblemMap map)
    {
        map = Resolve(code, outcome);

        return false;
    }
}
