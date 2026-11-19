using System.Collections.Generic;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     The exported contents of a refusal problem catalog for OpenAPI and documentation tooling.
/// </summary>
/// <param name="Mappings">Every code and prefix mapping with its contributor, ordered by pattern.</param>
/// <param name="InvalidDefault">The mapping answered for an invalid refusal with no code mapping.</param>
/// <param name="DeniedDefault">The mapping answered for a denied refusal with no code mapping.</param>
public sealed record RefusalProblemCatalogExport(
    IReadOnlyList<RefusalProblemEntry> Mappings,
    RefusalProblemMap InvalidDefault,
    RefusalProblemMap DeniedDefault);
