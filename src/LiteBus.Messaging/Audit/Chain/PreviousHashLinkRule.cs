using System;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Links an audit document by hashing its canonical bytes unchanged.
/// </summary>
/// <remarks>
///     <para>
///         The default <see cref="IAuditLinkRule" />: it returns <see cref="AuditLinkInput.CanonicalBytes" /> unchanged,
///         because the default canonical form already includes the segment, the sequence, and the previous hash, so the
///         predecessor is bound into every hash without further mixing.
///     </para>
///     <para>
///         An application whose canonical form leaves the chain position out replaces this rule with one that mixes
///         <see cref="AuditLinkInput.PreviousHash" /> and <see cref="AuditLinkInput.Sequence" /> into the hashed bytes.
///     </para>
/// </remarks>
public sealed class PreviousHashLinkRule : IAuditLinkRule
{
    /// <inheritdoc />
    public byte[] Link(AuditLinkInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.CanonicalBytes;
    }
}
