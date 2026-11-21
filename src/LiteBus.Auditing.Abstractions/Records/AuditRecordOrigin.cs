namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Where an audit record came from, so a record mapper can store a record no mediation produced by the hints it
///     carries instead of by mediation facts.
/// </summary>
/// <remarks>
///     A record that application code builds and writes through <see cref="IAuditTrail" /> is
///     <see cref="Application" /> unless the code sets otherwise, because that is the enum's default value. LiteBus sets
///     <see cref="Mediation" /> on every record it produces at the end of, or before, a mediation, and the value travels
///     with the record when it is delivered through the outbox.
/// </remarks>
public enum AuditRecordOrigin
{
    /// <summary>
    ///     Application code wrote the record directly, for something that happened outside one mediation, such as a
    ///     retention job, an operator correction or an import. Storage choices travel in
    ///     <see cref="AuditRecord.Properties" />.
    /// </summary>
    Application = 0,

    /// <summary>
    ///     LiteBus produced the record for a mediation of an audited message.
    /// </summary>
    Mediation = 1
}
