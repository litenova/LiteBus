using System.Globalization;
using LiteBus.Auditing.Abstractions;
using System.Text;
using LiteBus.Messaging.Abstractions.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Auditing.UnitTests.Hashing;

/// <summary>
///     Builds containers holding one canonicalizer for the test document.
/// </summary>
internal static class HashingTestServices
{
    /// <summary>
    ///     Builds a container holding one canonicalizer for the test document.
    /// </summary>
    /// <typeparam name="TCanonicalizer">The canonicalizer implementation.</typeparam>
    /// <returns>The built provider.</returns>
    internal static ServiceProvider ProviderWith<TCanonicalizer>()
        where TCanonicalizer : class, IAuditCanonicalizer<HashTestDocument>
    {
        return new ServiceCollection()
            .AddSingleton<IAuditCanonicalizer<HashTestDocument>, TCanonicalizer>()
            .BuildServiceProvider();
    }
}

/// <summary>
///     A stored audit document with one business field, standing in for an application-owned document type.
/// </summary>
internal sealed class HashTestDocument : IAuditChainDocument
{
    /// <inheritdoc />
    public string? Segment { get; set; }

    /// <inheritdoc />
    public long Sequence { get; set; }

    /// <inheritdoc />
    public string? PreviousHash { get; set; }

    /// <inheritdoc />
    public string? Hash { get; set; }

    /// <inheritdoc />
    public string? HashScheme { get; set; }

    /// <summary>
    ///     Gets or sets the business action recorded, the one tamper-evident field beyond the chain position.
    /// </summary>
    public string Action { get; set; } = string.Empty;
}

/// <summary>
///     The canonical form of <see cref="HashTestDocument" /> over the business field only.
/// </summary>
/// <remarks>
///     The bytes deliberately leave the segment, sequence, and previous hash out: this is the natural reading of
///     "your own canonical form", and it is what makes a pass-through link rule verify a chain that is not linked.
///     The framed rule binds the position around these bytes instead.
/// </remarks>
internal sealed class BusinessOnlyCanonicalizer : IAuditCanonicalizer<HashTestDocument>
{
    /// <inheritdoc />
    public byte[] Canonicalize(HashTestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Encoding.UTF8.GetBytes(string.Concat("action:", document.Action));
    }
}

/// <summary>
///     The canonical form of <see cref="HashTestDocument" /> including the chain position, as v1 records were written.
/// </summary>
/// <remarks>
///     The bytes include the segment, the sequence, and the previous hash, which is what lets the v1 pass-through
///     rule bind the predecessor. The scheme name stays out, so renaming a scheme never breaks the documents written
///     with it.
/// </remarks>
internal sealed class PositionBindingCanonicalizer : IAuditCanonicalizer<HashTestDocument>
{
    /// <inheritdoc />
    public byte[] Canonicalize(HashTestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Encoding.UTF8.GetBytes(string.Concat(
            document.Segment,
            "\n",
            document.Sequence.ToString(CultureInfo.InvariantCulture),
            "\n",
            document.PreviousHash,
            "\n",
            document.Action));
    }
}
