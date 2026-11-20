using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Recording;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Builds the audit hash schemes a chain writes and verifies with, by scheme name.
/// </summary>
/// <remarks>
///     <para>
///         The registry ships two schemes for every stored document type. <see cref="AuditHashSchemeNames.Sha256V1" />
///         keeps the pass-through <see cref="PreviousHashLinkRule" />, so records written before framing verify
///         unchanged. <see cref="AuditHashSchemeNames.Sha256V2" /> binds the chain position in a
///         <see cref="FramedLinkRule" />, so the canonicalizer only ever describes the record. Both hash with
///         <see cref="Sha256AuditHasher" />. Each scheme is built once per document type and then reused.
///     </para>
///     <para>
///         The canonicalizer comes from the container per document type: a storage package registers the default for
///         its document, and an application with its own document registers its own. A scheme that names no registered
///         canonicalizer fails composition, because a chain that cannot canonicalize cannot write or verify.
///     </para>
/// </remarks>
internal sealed class AuditHashSchemeRegistry : IAuditHashSchemeRegistry
{
    /// <summary>
    ///     The source label recorded for the shipped schemes, naming them in conflict messages.
    /// </summary>
    private const string ShippedSource = "the shipped audit hash schemes";

    /// <summary>
    ///     Resolves the canonicalizer per document type. Held for the life of the registry.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Guards the scheme cache. Registrations happen at composition and resolutions at runtime, so both take it.
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    ///     The registered and built schemes by document type and scheme name. Written under the registry lock.
    /// </summary>
    private readonly Dictionary<SchemeKey, RegisteredScheme> _schemes = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditHashSchemeRegistry" /> class.
    /// </summary>
    /// <param name="serviceProvider">The provider the canonicalizer per document type is resolved from.</param>
    public AuditHashSchemeRegistry(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _serviceProvider = serviceProvider;
    }

    /// <summary>
    ///     Registers an explicit hash scheme for a document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="scheme">The scheme to register, resolved by its name.</param>
    /// <param name="source">The source that supplied the scheme, named when a registration conflicts.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     A different scheme is already registered for the document type under the same name. The message names both
    ///     sources and points at <c>Replace</c> for a deliberate override.
    /// </exception>
    /// <remarks>
    ///     Each document type carries one scheme per name: two schemes under one name would leave verification
    ///     guessing which one a document was written with. Registering the same scheme instance again returns without
    ///     effect, while a conflicting repeat fails composition naming both sources.
    /// </remarks>
    public void Register<TDocument>(AuditHashScheme<TDocument> scheme, string source)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var key = new SchemeKey(typeof(TDocument), scheme.Name);

        lock (_sync)
        {
            if (_schemes.TryGetValue(key, out var existing))
            {
                RegistrationConflict.ThrowIfConflicting(
                    existing.Scheme,
                    (object) scheme,
                    $"Audit hash scheme '{scheme.Name}' for '{typeof(TDocument).Name}'",
                    existing.Source,
                    source);

                return;
            }

            _schemes[key] = new RegisteredScheme(scheme, source);
        }
    }

    /// <summary>
    ///     Registers an explicit hash scheme for a document type, overwriting any scheme already registered under that
    ///     name.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="scheme">The scheme to register, resolved by its name.</param>
    /// <param name="source">The source that supplied the scheme, named when a later registration conflicts.</param>
    /// <remarks>
    ///     This is the deliberate override for the registration conflict rule. A conflicting repeat through
    ///     <c>Register</c> fails composition, while this method overwrites it.
    /// </remarks>
    public void Replace<TDocument>(AuditHashScheme<TDocument> scheme, string source)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var key = new SchemeKey(typeof(TDocument), scheme.Name);

        lock (_sync)
        {
            _schemes[key] = new RegisteredScheme(scheme, source);
        }
    }

    /// <inheritdoc />
    public AuditHashScheme<TDocument> Resolve<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);

        var key = new SchemeKey(typeof(TDocument), schemeName);

        lock (_sync)
        {
            if (_schemes.TryGetValue(key, out var existing))
            {
                return (AuditHashScheme<TDocument>) existing.Scheme;
            }

            var (built, source) = BuildScheme<TDocument>(schemeName);
            _schemes[key] = new RegisteredScheme(built, source);

            return built;
        }
    }

    /// <summary>
    ///     Builds a scheme the application defined on the chain builder, or a shipped one.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="schemeName">The scheme name.</param>
    /// <returns>The scheme and the registration source it is recorded under.</returns>
    private (AuditHashScheme<TDocument> Scheme, string Source) BuildScheme<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument
    {
        if (_serviceProvider.GetService(typeof(AuditHashChainOptions)) is AuditHashChainOptions chain &&
            chain.CustomSchemes.TryGetValue(schemeName, out var definition))
        {
            var scheme = new AuditHashScheme<TDocument>(
                schemeName,
                RequireCanonicalizer<TDocument>(schemeName),
                definition.LinkRule ?? new FramedLinkRule(schemeName),
                definition.Hasher);

            return (scheme, "AuditHashChainBuilder.AddScheme");
        }

        return (BuildShippedScheme<TDocument>(schemeName), ShippedSource);
    }

    /// <summary>
    ///     Builds one shipped scheme for a document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="schemeName">The stable scheme name documents store.</param>
    /// <returns>The scheme: the canonical form, link rule, and hasher the name selects.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     The name is not a shipped scheme, or no <see cref="IAuditCanonicalizer{TDocument}" /> is registered for the
    ///     document type.
    /// </exception>
    private AuditHashScheme<TDocument> BuildShippedScheme<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument
    {
        var isV1 = string.Equals(schemeName, AuditHashSchemeNames.Sha256V1, StringComparison.Ordinal);
        var isV2 = string.Equals(schemeName, AuditHashSchemeNames.Sha256V2, StringComparison.Ordinal);

        if (!isV1 && !isV2)
        {
            throw new LiteBusConfigurationException(
                $"Unknown audit hash scheme '{schemeName}' for '{typeof(TDocument).Name}'. The shipped schemes are " +
                $"'{AuditHashSchemeNames.Sha256V1}' and '{AuditHashSchemeNames.Sha256V2}'; define any other scheme " +
                "with AddScheme on the hash chain builder before writing with it.");
        }

        var canonicalizer = RequireCanonicalizer<TDocument>(schemeName);

        IAuditLinkRule linkRule = isV1
            ? new PreviousHashLinkRule()
            : new FramedLinkRule(schemeName);

        return new AuditHashScheme<TDocument>(schemeName, canonicalizer, linkRule, new Sha256AuditHasher());
    }

    /// <summary>
    ///     Resolves the canonicalizer registered for the stored document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="schemeName">The scheme being built, named in the error.</param>
    /// <returns>The canonicalizer.</returns>
    /// <exception cref="LiteBusConfigurationException">No canonicalizer is registered for the document type.</exception>
    private IAuditCanonicalizer<TDocument> RequireCanonicalizer<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument
    {
        return _serviceProvider.GetService(typeof(IAuditCanonicalizer<TDocument>)) as IAuditCanonicalizer<TDocument>
               ?? throw new LiteBusConfigurationException(
                   $"Audit hash scheme '{schemeName}' for '{typeof(TDocument).Name}' needs an " +
                   $"'{typeof(IAuditCanonicalizer<TDocument>).Name}' registered with the application container. A " +
                   "storage package registers the default for its document; an application with its own document " +
                   "registers its own.");
    }

    /// <summary>
    ///     Identifies one scheme in the cache: the document type it hashes and the name documents store.
    /// </summary>
    /// <param name="DocumentType">The stored audit document type.</param>
    /// <param name="SchemeName">The stable scheme name documents store, compared ordinally.</param>
    private readonly record struct SchemeKey(Type DocumentType, string SchemeName);

    /// <summary>
    ///     A cached scheme and the source that supplied it, named when a registration conflicts.
    /// </summary>
    /// <param name="Scheme">The registered or built scheme.</param>
    /// <param name="Source">The source that supplied the scheme.</param>
    private sealed record RegisteredScheme(object Scheme, string Source);
}
