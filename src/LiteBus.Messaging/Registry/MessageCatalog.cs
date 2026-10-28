using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     An <see cref="IMessageCatalog" /> over the message registry, materialised once when a composition check runs.
/// </summary>
/// <remarks>
///     Materialised rather than lazy because a check enumerates it several times, and because it runs once at
///     composition where the registry is no longer changing. Abstract types and interfaces are excluded: they are
///     shapes rather than messages, which is the same rule the declaration requirements apply.
/// </remarks>
internal sealed class MessageCatalog : IMessageCatalog
{
    /// <summary>
    ///     The entries, one per concrete registered message type.
    /// </summary>
    private readonly List<MessageCatalogEntry> _entries;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageCatalog" /> class.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    public MessageCatalog(IMessageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _entries = [];

        foreach (var descriptor in reader)
        {
            if (descriptor.MessageType.IsAbstract || descriptor.MessageType.IsInterface)
            {
                continue;
            }

            _entries.Add(new MessageCatalogEntry(descriptor.MessageType, descriptor.Metadata));
        }
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public IEnumerable<MessageCatalogEntry> Audited()
    {
        foreach (var entry in _entries)
        {
            if (entry.Audit is not null)
            {
                yield return entry;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerator<MessageCatalogEntry> GetEnumerator()
    {
        return _entries.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
