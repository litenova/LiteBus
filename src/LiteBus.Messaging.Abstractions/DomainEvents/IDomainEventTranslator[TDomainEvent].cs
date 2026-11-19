using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Maps one domain event to zero or more published messages.
/// </summary>
/// <typeparam name="TDomainEvent">The domain event type this translator handles.</typeparam>
public interface IDomainEventTranslator<in TDomainEvent>
{
    /// <summary>
    ///     Translates one domain event into the messages to stage.
    /// </summary>
    /// <param name="domainEvent">The harvested or collected domain event.</param>
    /// <param name="context">Services and ambient metadata available during translation.</param>
    /// <param name="cancellationToken">The token used to cancel translation.</param>
    /// <returns>The messages to stage; an empty list stages nothing.</returns>
    ValueTask<IReadOnlyList<object>> TranslateAsync(
        TDomainEvent domainEvent,
        DomainEventTranslationContext context,
        CancellationToken cancellationToken);
}
