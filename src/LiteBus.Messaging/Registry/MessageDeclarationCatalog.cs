using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Collects the declarations one <see cref="IMultiMessageDefinition" /> contributes while
///     <see cref="IMultiMessageDefinition.Describe" /> runs.
/// </summary>
/// <remarks>
///     One instance per source. Each <c>Describe</c> call gets its own <see cref="MessageDeclarationCollector" />, so
///     a declaration written for one message cannot leak into another, and a duplicate value for one message fails
///     the same way a duplicate in one <c>Describe</c> body does.
/// </remarks>
internal sealed class MessageDeclarationCatalog : IMessageDeclarationCatalog
{
    /// <summary>
    ///     The source type that is describing, named in configuration diagnostics.
    /// </summary>
    private readonly Type _sourceType;

    /// <summary>
    ///     The declarations collected so far, in the order they were declared.
    /// </summary>
    private readonly List<MessageDeclaration> _declarations = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDeclarationCatalog" /> class.
    /// </summary>
    /// <param name="sourceType">The catalog source type that is describing.</param>
    public MessageDeclarationCatalog(Type sourceType)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        _sourceType = sourceType;
    }

    /// <inheritdoc />
    public IMessageDeclarationCatalog Describe<TMessage>(Action<IMessageDeclarations> describe)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(describe);
        return DescribeMessage(typeof(TMessage), describe, replace: false);
    }

    /// <inheritdoc />
    public IMessageDeclarationCatalog Describe(Type messageType, Action<IMessageDeclarations> describe)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(describe);
        return DescribeMessage(messageType, describe, replace: false);
    }

    /// <inheritdoc />
    public IMessageDeclarationCatalog Replace<TMessage>(Action<IMessageDeclarations> describe)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(describe);
        return DescribeMessage(typeof(TMessage), describe, replace: true);
    }

    /// <inheritdoc />
    public IMessageDeclarationCatalog Replace(Type messageType, Action<IMessageDeclarations> describe)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(describe);
        return DescribeMessage(messageType, describe, replace: true);
    }

    /// <summary>
    ///     Produces every declaration the source contributed.
    /// </summary>
    /// <returns>The declarations to apply to their messages.</returns>
    /// <exception cref="MessageDeclarationException">The source declared nothing.</exception>
    public IReadOnlyList<MessageDeclaration> Collect()
    {
        if (_declarations.Count == 0)
        {
            throw new MessageDeclarationException(
                $"The declaration source '{_sourceType.Name}' declared nothing. Declare at least one message with "
                + "Describe, or delete the source: a source that declares nothing is indistinguishable from one "
                + "nobody finished.");
        }

        return _declarations;
    }

    /// <summary>
    ///     Records the declarations one <c>Describe</c> call contributes for one message type.
    /// </summary>
    /// <param name="messageType">The message type the declarations apply to.</param>
    /// <param name="describe">Receives the collection that records every declaration for the message.</param>
    /// <param name="replace">
    ///     <see langword="true" /> when the call deliberately overwrites the recorded values.
    /// </param>
    /// <returns>The catalog, for chaining.</returns>
    /// <exception cref="MessageDeclarationException">
    ///     The source declared the same value type twice for this message without replacing it.
    /// </exception>
    private MessageDeclarationCatalog DescribeMessage(Type messageType, Action<IMessageDeclarations> describe, bool replace)
    {
        var normalized = messageType.NormalizeMessageRegistrationType();
        var collector = new MessageDeclarationCollector(normalized, _sourceType);
        describe(collector);

        foreach (var declaration in collector.Collect())
        {
            Add(new MessageDeclaration(
                declaration.MessageType,
                declaration.KeyType,
                declaration.Value,
                _sourceType,
                MetadataSourceKind.Source,
                replace));
        }

        return this;
    }

    /// <summary>
    ///     Records one declaration, rejecting a second declaration of the same value type for the same message.
    /// </summary>
    /// <param name="declaration">The declaration to record.</param>
    /// <exception cref="MessageDeclarationException">
    ///     The source declared the same value type twice for this message.
    /// </exception>
    /// <remarks>
    ///     Caught here rather than at the registry, because two <c>Describe</c> calls for one message in one source
    ///     body are a typo the author can see, and the registry's own duplicate check would name two sources where
    ///     there is only one. A <c>Replace</c> call overwrites the earlier entry instead, which is what makes a
    ///     deliberate override inside one source read as one decision.
    /// </remarks>
    private void Add(MessageDeclaration declaration)
    {
        for (var index = 0; index < _declarations.Count; index++)
        {
            var existing = _declarations[index];

            if (existing.MessageType != declaration.MessageType || existing.KeyType != declaration.KeyType)
            {
                continue;
            }

            if (declaration.Replace)
            {
                _declarations[index] = declaration;
                return;
            }

            throw new MessageDeclarationException(
                $"The declaration source '{_sourceType.Name}' declares '{declaration.KeyType.Name}' twice for "
                + $"'{declaration.MessageType.Name}'. Metadata holds one value per type, so the second would silently "
                + "replace the first. Call Replace to overwrite it deliberately.");
        }

        _declarations.Add(declaration);
    }
}
