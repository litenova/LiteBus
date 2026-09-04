using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.MediationStrategies;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Dependencies;

namespace LiteBus.Testing;

/// <summary>
///     Runs the real mediation pipeline over hand-supplied handlers, with no host and no container.
/// </summary>
/// <typeparam name="TMessage">The message type under test.</typeparam>
/// <remarks>
///     <para>
///         Asserting that a guard denies previously meant booting the whole host, which for an application with a
///         relational store meant a database container for a test about one authorization decision.
///     </para>
///     <para>
///         It runs the shipped strategies through the shipped stage runner, so what it proves is what the pipeline
///         does rather than a model of it: the fixed stage order, the aggregation policy of the validator stage, and
///         the priority ordering all apply. What it leaves out is composition, so nothing here validates a
///         registration a host would reject; assert that against a host.
///     </para>
///     <para>
///         Handlers are supplied as instances rather than resolved, which is the point: a test builds the one guard
///         it is testing with the doubles it wants and hands it over.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// var result = await MediationHarness.For<CloseOrganizationCommand>()
///     .With(new AuthorizationGuard<CloseOrganizationCommand>(metadata, authorizer))
///     .RunAsync(command);
///
/// result.Outcome.Should().Be(MediationOutcome.Denied);
/// result.StagesRun.Should().Equal(PreStage.Guard);
/// result.MainHandlerRan.Should().BeFalse();
/// ]]></code>
/// </example>
public sealed class MediationHarness<TMessage>
    where TMessage : notnull
{
    /// <summary>
    ///     The handler instances the pipeline resolves, keyed by their concrete type.
    /// </summary>
    private readonly Dictionary<Type, object> _handlers = [];

    /// <summary>
    ///     The registry the pipeline reads, one per harness so nothing leaks between tests.
    /// </summary>
    private readonly IMessageRegistry _registry = MessageRegistryFactory.Create();

    /// <summary>
    ///     The tags the mediation runs under.
    /// </summary>
    private readonly List<string> _tags = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MediationHarness{TMessage}" /> class.
    /// </summary>
    internal MediationHarness()
    {
        _registry.Register(typeof(TMessage));
    }

    /// <summary>
    ///     Adds one handler instance to the pipeline.
    /// </summary>
    /// <param name="handler">The pre-stage handler, main handler, post-handler, or completion handler to run.</param>
    /// <returns>The harness, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handler" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Registered by its concrete type, so the registry discovers every contract it implements exactly as it
    ///     would in a host. A class that both denies and answers is run once per stage, here as there.
    /// </remarks>
    public MediationHarness<TMessage> With(object handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[handler.GetType()] = handler;
        _registry.Register(handler.GetType());

        return this;
    }

    /// <summary>
    ///     Adds several handler instances to the pipeline.
    /// </summary>
    /// <param name="handlers">The handlers to run.</param>
    /// <returns>The harness, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="handlers" /> is <see langword="null" />.</exception>
    public MediationHarness<TMessage> With(params object[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        foreach (var handler in handlers)
        {
            With(handler);
        }

        return this;
    }

    /// <summary>
    ///     Runs the mediation under the given tags, so tag filtering can be asserted.
    /// </summary>
    /// <param name="tags">The mediation tags.</param>
    /// <returns>The harness, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tags" /> is <see langword="null" />.</exception>
    public MediationHarness<TMessage> WithTags(params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _tags.AddRange(tags);
        return this;
    }

    /// <summary>
    ///     Runs the pipeline for a message that produces no result.
    /// </summary>
    /// <param name="message">The message to mediate.</param>
    /// <param name="cancellationToken">The cancellation token passed to the mediation.</param>
    /// <returns>How the mediation ended and which stages ran.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     A refusal is reported in the result rather than raised, because a test asserting on a denial should not
    ///     have to catch. A genuine fault still propagates, so a broken handler fails the test as a fault.
    /// </remarks>
    public async Task<MediationHarnessResult> RunAsync(
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var capture = new MediationEndingCapture();
        var mediator = CreateMediator();

        var request = new MessageMediationRequest<TMessage, Task>
        {
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<TMessage>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = _tags,
            Items = new Dictionary<string, object> { [MediationEndingCapture.ItemKey] = capture }
        };

        try
        {
            await mediator.Mediate(message, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            // Reported in the result. A test asserting a denial should read it, not catch it.
        }

        return Describe(capture, value: null, hasValue: false);
    }

    /// <summary>
    ///     Runs the pipeline for a message that produces a result.
    /// </summary>
    /// <typeparam name="TMessageResult">The result type the message declares.</typeparam>
    /// <param name="message">The message to mediate.</param>
    /// <param name="cancellationToken">The cancellation token passed to the mediation.</param>
    /// <returns>How the mediation ended, which stages ran, and the value it produced.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message" /> is <see langword="null" />.</exception>
    public async Task<MediationHarnessResult> RunAsync<TMessageResult>(
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var capture = new MediationEndingCapture();
        var mediator = CreateMediator();

        var request = new MessageMediationRequest<TMessage, Task<TMessageResult>>
        {
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<TMessage, TMessageResult>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = _tags,
            Items = new Dictionary<string, object> { [MediationEndingCapture.ItemKey] = capture }
        };

        try
        {
            var value = await mediator.Mediate(message, request, cancellationToken).ConfigureAwait(false);
            return Describe(capture, value, hasValue: true);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            return Describe(capture, value: null, hasValue: false);
        }
    }

    /// <summary>
    ///     Asks the decision stages what they would say, without performing the message.
    /// </summary>
    /// <param name="message">The message to evaluate.</param>
    /// <param name="cancellationToken">The cancellation token passed to the evaluation.</param>
    /// <returns>The decision the guard and validator stages reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message" /> is <see langword="null" />.</exception>
    public Task<MediationDecision> EvaluateAsync(
        TMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = new MessageMediationRequest<TMessage, Task<MediationDecision>>
        {
            MessageMediationStrategy = new DecisionEvaluationMediationStrategy<TMessage>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = _tags
        };

        return CreateMediator().Mediate(message, request, cancellationToken);
    }

    /// <summary>
    ///     Builds the result from what the pipeline recorded.
    /// </summary>
    /// <param name="capture">The ending the strategy recorded.</param>
    /// <param name="value">The value the mediation produced, when it produced one.</param>
    /// <param name="hasValue">Whether a value was produced.</param>
    /// <returns>The harness result.</returns>
    /// <remarks>
    ///     The main handler ran when nothing stopped the pipeline, which is the same condition the strategy uses. It
    ///     is reported separately from the outcome because that is the assertion a test of a guard wants to make.
    /// </remarks>
    private static MediationHarnessResult Describe(MediationEndingCapture capture, object? value, bool hasValue)
    {
        return new MediationHarnessResult
        {
            Outcome = capture.Outcome,
            StagesRun = capture.StagesRun.ToList(),
            Reason = capture.Reason,
            Code = capture.Code,
            Failures = capture.Failures,
            Value = hasValue ? value : null,
            MainHandlerRan = capture.Outcome == MediationOutcome.Succeeded
        };
    }

    /// <summary>
    ///     Builds the mediator over this harness's registry and handler instances.
    /// </summary>
    /// <returns>The mediator that runs the shipped pipeline.</returns>
    private IMessageMediator CreateMediator()
    {
        var provider = new HarnessServiceProvider(_handlers);

        return MessageMediatorFactory.Create(_registry, new RootMessageDispatchScopeFactory(provider));
    }

    /// <summary>
    ///     Resolves the handler instances the harness was given, and nothing else.
    /// </summary>
    /// <remarks>
    ///     A handler whose constructor dependency was not supplied is a test that forgot to build it, and returning
    ///     null here reports that as a resolution failure naming the type rather than as a null reference somewhere
    ///     inside the pipeline.
    /// </remarks>
    private sealed class HarnessServiceProvider : IServiceProvider
    {
        /// <summary>
        ///     The instances the harness was given, keyed by concrete type.
        /// </summary>
        private readonly Dictionary<Type, object> _handlers;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HarnessServiceProvider" /> class.
        /// </summary>
        /// <param name="handlers">The instances the harness was given.</param>
        public HarnessServiceProvider(Dictionary<Type, object> handlers)
        {
            _handlers = handlers;
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return _handlers.GetValueOrDefault(serviceType);
        }
    }
}
