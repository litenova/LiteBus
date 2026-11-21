namespace LiteBus.Auditing.Hashing;

/// <summary>
///     The scheme names the audit hash scheme registry ships with.
/// </summary>
/// <remarks>
///     Each stored document carries the name of the scheme it was written with, and verification picks the scheme per
///     document, so a chain can move to a new scheme without rewriting its history.
/// </remarks>
public static class AuditHashSchemeNames
{
    /// <summary>
    ///     Gets the first shipped scheme: the canonical form already includes the chain position, and
    ///     <see cref="LiteBus.Auditing.Hashing.PreviousHashLinkRule" /> passes it through unchanged.
    /// </summary>
    /// <value>
    ///     The stable name <c>sha256-v1</c>, stored on every document written with the scheme.
    /// </value>
    public const string Sha256V1 = "sha256-v1";

    /// <summary>
    ///     Gets the current shipped scheme: <see cref="FramedLinkRule" /> binds the scheme, segment, sequence, and
    ///     previous hash around the canonical business bytes, so a canonicalizer only ever describes the record.
    /// </summary>
    /// <value>
    ///     The stable name <c>sha256-v2</c>, stored on every document written with the scheme.
    /// </value>
    public const string Sha256V2 = "sha256-v2";
}
