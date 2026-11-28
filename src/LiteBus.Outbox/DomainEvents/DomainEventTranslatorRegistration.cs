using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.DomainEvents;

/// <summary>
///     Translates one domain event type into the messages to stage, either through a registered function or through a translator resolved from dependency injection.
/// </summary>
/// <param name="EventType">The closed domain event type this registration translates.</param>
/// <param name="TranslatorType">The translator type resolved from the dispatch scope, or <see langword="null" /> when a function translates the event.</param>
/// <param name="TranslateFunction">The registered translation function, or <see langword="null" /> when a translator type translates the event.</param>
/// <param name="Description">The builder call shape recorded for conflict messages, such as <c>TranslateDomainEvent&lt;OrderPlaced&gt;</c>.</param>
/// <param name="TranslateAsync">Translates one domain event into the messages to stage.</param>
internal sealed record DomainEventTranslatorRegistration(
    Type EventType,
    Type? TranslatorType,
    Delegate? TranslateFunction,
    string Description,
    Func<object, DomainEventTranslationContext, CancellationToken, ValueTask<IReadOnlyList<object>>> TranslateAsync)
{
    /// <summary>
    ///     Creates a registration that translates through a pure function.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type this registration translates.</typeparam>
    /// <param name="translate">Maps one domain event to the messages to stage.</param>
    /// <returns>The registration stored on the decisions.</returns>
    internal static DomainEventTranslatorRegistration ForFunction<TEvent>(Func<TEvent, IReadOnlyList<object>> translate)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(translate);

        var eventType = ThrowIfOpenEventType(typeof(TEvent));

        return new DomainEventTranslatorRegistration(
            eventType,
            null,
            translate,
            $"TranslateDomainEvent<{eventType.FullName}>",
            (domainEvent, _, _) => new ValueTask<IReadOnlyList<object>>(translate((TEvent) domainEvent)));
    }

    /// <summary>
    ///     Creates a registration that translates through a translator resolved from the dispatch scope.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type this registration translates.</typeparam>
    /// <typeparam name="TTranslator">The translator type resolved from the dispatch scope.</typeparam>
    /// <returns>The registration stored on the decisions.</returns>
    internal static DomainEventTranslatorRegistration ForTranslator<TEvent, TTranslator>()
        where TEvent : notnull
        where TTranslator : class, IDomainEventTranslator<TEvent>
    {
        var eventType = ThrowIfOpenEventType(typeof(TEvent));
        var translatorType = typeof(TTranslator);

        if (translatorType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                "Domain event translators must use a closed translator type. Register each closed translator shape explicitly.",
                nameof(TTranslator));
        }

        return new DomainEventTranslatorRegistration(
            eventType,
            translatorType,
            null,
            $"TranslateDomainEvent<{eventType.FullName}, {translatorType.FullName}>",
            TranslateWith<TEvent, TTranslator>);
    }

    /// <summary>
    ///     Rejects an open generic domain event type, which can never match a collected instance.
    /// </summary>
    /// <param name="eventType">The domain event type one builder call registers.</param>
    /// <returns>The same event type, for chaining.</returns>
    internal static Type ThrowIfOpenEventType(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        if (eventType.ContainsGenericParameters)
        {
            throw new ArgumentException(
                "Domain event decisions must use a closed event type. Register each closed event shape with its own decision.",
                nameof(eventType));
        }

        return eventType;
    }

    /// <summary>
    ///     Resolves the translator from the dispatch scope and translates one domain event.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type this registration translates.</typeparam>
    /// <typeparam name="TTranslator">The translator type resolved from the dispatch scope.</typeparam>
    /// <param name="domainEvent">The harvested or collected domain event.</param>
    /// <param name="context">Services and ambient metadata available during translation.</param>
    /// <param name="cancellationToken">The token used to cancel translation.</param>
    /// <returns>The messages to stage; an empty list stages nothing.</returns>
    private static async ValueTask<IReadOnlyList<object>> TranslateWith<TEvent, TTranslator>(
        object domainEvent,
        DomainEventTranslationContext context,
        CancellationToken cancellationToken)
        where TEvent : notnull
        where TTranslator : class, IDomainEventTranslator<TEvent>
    {
        var translator = context.ServiceProvider.GetService(typeof(TTranslator)) as IDomainEventTranslator<TEvent>;

        if (translator is null)
        {
            throw new MessageFaultException(
                context.MediatedMessageType,
                DomainEventFaultCodes.TranslatorMissing,
                $"No translator '{typeof(TTranslator).FullName}' is registered for domain event '{typeof(TEvent).FullName}'. " +
                "Register the translator in dependency injection before collecting the domain event.");
        }

        return await translator.TranslateAsync((TEvent) domainEvent, context, cancellationToken).ConfigureAwait(false);
    }
}
