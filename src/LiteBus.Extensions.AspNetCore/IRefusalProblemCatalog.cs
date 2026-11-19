namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     Maps mediation refusal codes to HTTP status codes and problem titles.
/// </summary>
/// <remarks>
///     <para>
///         An application contributes its own codes with <see cref="Map" /> and registers the catalog in request
///         services, so <see cref="MediationResultHttpExtensions" /> answers product-specific refusals with the status
///         the product chooses. Codes without a mapping answer <c>400 Bad Request</c>.
///     </para>
///     <para>
///         A code ending in <c>.*</c> is a prefix pattern that covers every code under that prefix, such as
///         <c>authorization.*</c> covering <c>authorization.forbidden</c>. An exact mapping wins over a prefix pattern,
///         and the longest prefix wins when several patterns cover one code.
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
    /// <returns>The catalog for chaining.</returns>
    IRefusalProblemCatalog Map(string code, int statusCode, string title);

    /// <summary>
    ///     Resolves the mapping for a refusal code.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <returns>The matching mapping, or the fallback mapping when nothing matches.</returns>
    RefusalProblemMap Resolve(string? code);

    /// <summary>
    ///     Tries to resolve the mapping for a refusal code, reporting whether a mapping matches.
    /// </summary>
    /// <param name="code">The refusal code, or <see langword="null" /> when the decision supplied none.</param>
    /// <param name="map">The matching mapping, or the fallback mapping when nothing matches.</param>
    /// <returns><see langword="true" /> when a mapping matches; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     <para>
    ///         The default implementation calls <see cref="Resolve" /> and returns <see langword="true" /> when
    ///         <paramref name="code" /> is not <see langword="null" />. It cannot tell a fallback from a match, so an
    ///         application that answers an unmapped code as a server error uses
    ///         <see cref="RefusalProblemCatalog" />, whose override returns <see langword="false" /> for the fallback
    ///         path.
    ///     </para>
    /// </remarks>
    bool TryResolve(string? code, out RefusalProblemMap map)
    {
        map = Resolve(code);

        return code is not null;
    }
}
