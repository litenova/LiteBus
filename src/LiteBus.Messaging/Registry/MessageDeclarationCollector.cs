using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Collects the declarations one <see cref="IMessageDefinition{TMessage}" /> contributes while
///     <see cref="IMessageDefinition{TMessage}.Describe" /> runs.
/// </summary>
/// <remarks>
///     One instance per definition per message type. It records the value type each declaration is keyed by, which is
///     the same key the compiler-typed shape uses, so the registry applies both through one path.
/// </remarks>
internal sealed class MessageDeclarationCollector : IMessageDeclarations
{
    /// <summary>
    ///     The definition type that is describing, named in configuration diagnostics.
    /// </summary>
    private readonly Type _definitionType;

    /// <summary>
    ///     The exemptions collected so far, aggregated into one value because metadata holds one per key type.
    /// </summary>
    private readonly List<DeclarationExemption> _exemptions = [];

    /// <summary>
    ///     The message type the declarations apply to.
    /// </summary>
    private readonly Type _messageType;

    /// <summary>
    ///     The declarations collected so far, in the order they were declared.
    /// </summary>
    private readonly List<MessageDeclaration> _values = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageDeclarationCollector" /> class.
    /// </summary>
    /// <param name="messageType">The message type the declarations apply to.</param>
    /// <param name="definitionType">The definition type that is describing.</param>
    public MessageDeclarationCollector(Type messageType, Type definitionType)
    {
        _messageType = messageType;
        _definitionType = definitionType;
    }

    /// <inheritdoc />
    public IMessageDeclarations Declare<TValue>(TValue value)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        Add(typeof(TValue), value);
        return this;
    }

    /// <inheritdoc />
    public IMessageDeclarations Audited(
        string action,
        string? category = null,
        string? targetKind = null,
        bool reasonRequired = false)
    {
        var declaration = AuditDeclaration.Audited(action) with
        {
            Category = category,
            TargetKind = targetKind,
            ReasonRequired = reasonRequired
        };

        Add(typeof(AuditDeclaration), declaration);
        return this;
    }

    /// <inheritdoc />
    public IMessageDeclarations NotAudited(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);

        // Both halves, for the same reason [AuditExempt] records both: the declaration is what the record writer
        // reads to know the message is exempt, and the exemption is what every requirement and catalogue reads.
        Add(typeof(AuditDeclaration), AuditDeclaration.Exempt(rationale));
        _exemptions.Add(new DeclarationExemption(typeof(AuditDeclaration), rationale));
        return this;
    }

    /// <inheritdoc />
    public IMessageDeclarations Exempt<TValue>(string rationale)
        where TValue : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        _exemptions.Add(new DeclarationExemption(typeof(TValue), rationale));
        return this;
    }

    /// <summary>
    ///     Produces every declaration the definition contributed, including the aggregated exemption set.
    /// </summary>
    /// <returns>The declarations to apply to the message.</returns>
    /// <exception cref="MessageDeclarationException">The definition declared nothing.</exception>
    public IReadOnlyList<MessageDeclaration> Collect()
    {
        if (_exemptions.Count > 0)
        {
            Add(typeof(DeclarationExemptions), new DeclarationExemptions(_exemptions));
        }

        if (_values.Count == 0)
        {
            throw new MessageDeclarationException(
                $"The message definition '{_definitionType.Name}' declared nothing for '{_messageType.Name}'. Declare "
                + "at least one value in Describe, or delete the definition: a definition that declares nothing is "
                + "indistinguishable from one nobody finished.");
        }

        return _values;
    }

    /// <summary>
    ///     Records one declaration, rejecting a second declaration of the same value type.
    /// </summary>
    /// <param name="valueType">The metadata value type the declaration is keyed by.</param>
    /// <param name="value">The declared value.</param>
    /// <exception cref="MessageDeclarationException">
    ///     The definition declared the same value type twice for this message.
    /// </exception>
    /// <remarks>
    ///     Caught here rather than at the registry, because two calls in one <c>Describe</c> body are a typo the author
    ///     can see, and the registry's own duplicate check would name two definitions where there is only one.
    /// </remarks>
    private void Add(Type valueType, object value)
    {
        foreach (var existing in _values)
        {
            if (existing.KeyType != valueType)
            {
                continue;
            }

            throw new MessageDeclarationException(
                $"The message definition '{_definitionType.Name}' declares '{valueType.Name}' twice for "
                + $"'{_messageType.Name}'. Metadata holds one value per type, so the second would silently replace the "
                + "first.");
        }

        _values.Add(new MessageDeclaration(_messageType, valueType, value, _definitionType));
    }
}
