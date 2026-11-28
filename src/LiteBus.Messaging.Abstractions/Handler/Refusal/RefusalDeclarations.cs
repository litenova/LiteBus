using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The refusal codes one message declares it can produce.
/// </summary>
/// <remarks>
///     <para>
///         This is one metadata value holding many declarations, rather than one value per code, because metadata is
///         keyed by value type and a message may refuse for several reasons at once.
///     </para>
///     <para>
///         Declare codes with <see cref="IMessageDeclarations.Refusals" />, which stores this value the way any
///         other declaration is stored. At composition, every message's declarations are harvested into the refusal
///         catalog, so transports answer the codes without a hand-registered map. Declaring the same code twice
///         with an equal kind, title, and retryability keeps the first; declaring it with a different meaning
///         throws, because one of them would have to be discarded and nothing says which.
///     </para>
/// </remarks>
public sealed class RefusalDeclarations
{
    /// <summary>
    ///     The suffix that marks a code as a prefix pattern.
    /// </summary>
    private const string PrefixSuffix = ".*";

    /// <summary>
    ///     The declarations keyed by code.
    /// </summary>
    private readonly Dictionary<string, RefusalDeclaration> _byCode;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RefusalDeclarations" /> class.
    /// </summary>
    /// <param name="declarations">The refusal codes the message declares.</param>
    /// <exception cref="ArgumentNullException"><paramref name="declarations" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     A declaration carries a blank code or title, a malformed pattern, or a code another declaration in the
    ///     same value already describes differently.
    /// </exception>
    public RefusalDeclarations(IEnumerable<RefusalDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        _byCode = new Dictionary<string, RefusalDeclaration>(StringComparer.Ordinal);

        foreach (var declaration in declarations)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            Validate(declaration);

            if (_byCode.TryGetValue(declaration.Code, out var existing))
            {
                if (existing.Equals(declaration))
                {
                    continue;
                }

                throw new ArgumentException(
                    $"The refusal code '{declaration.Code}' is declared twice with different meanings. Declare it once.",
                    nameof(declarations));
            }

            _byCode.Add(declaration.Code, declaration);
        }
    }

    /// <summary>
    ///     Gets every refusal code the message declares.
    /// </summary>
    public IReadOnlyCollection<RefusalDeclaration> Values => _byCode.Values;

    /// <summary>
    ///     Validates one declaration before it is recorded.
    /// </summary>
    /// <param name="declaration">The declaration to validate.</param>
    /// <exception cref="ArgumentException">
    ///     The declaration carries a blank code or title, or a code containing <c>*</c> anywhere but as a
    ///     trailing <c>.*</c> with a non-empty prefix.
    /// </exception>
    private static void Validate(RefusalDeclaration declaration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Code);
        ArgumentNullException.ThrowIfNull(declaration.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.Title);

        if (declaration.Code.Contains('*') && !IsPrefixPattern(declaration.Code))
        {
            throw new ArgumentException(
                "A refusal code containing '*' must end with '.*' and name a non-empty prefix, such as 'authorization.*'.",
                nameof(declaration));
        }
    }

    /// <summary>
    ///     Determines whether a code covers a prefix rather than one exact code.
    /// </summary>
    /// <param name="code">The refusal code.</param>
    /// <returns><see langword="true" /> when the code ends in <c>.*</c> with a non-empty prefix.</returns>
    private static bool IsPrefixPattern(string code)
    {
        return code.EndsWith(PrefixSuffix, StringComparison.Ordinal) && code.Length > PrefixSuffix.Length;
    }
}
