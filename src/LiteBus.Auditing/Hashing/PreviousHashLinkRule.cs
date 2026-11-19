using System;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Links an audit document by hashing its canonical bytes unchanged.
/// </summary>
/// <remarks>
///     <para>
///         The <c>sha256-v1</c> <see cref="IAuditLinkRule" />: it returns <see cref="AuditLinkInput.CanonicalBytes" />
///         unchanged, because the v1 canonical form already includes the segment, the sequence, and the previous hash,
///         so the predecessor is bound into every hash without further mixing. Records written with this rule verify
///         unchanged.
///     </para>
///     <para>
///         The pass-through is sound only when the canonical form binds the chain position. A canonical form over
///         business fields only would verify without being linked: records could be reordered or replaced without a
///         break. New schemes bind the position in the rule itself through
///         <c>LiteBus.Auditing.Hashing.FramedLinkRule</c>, which frames the scheme, segment, sequence, and previous
///         hash around the canonical business bytes.
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
