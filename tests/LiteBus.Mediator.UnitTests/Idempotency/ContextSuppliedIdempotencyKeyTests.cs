using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies idempotency for a command whose key cannot be projected from the message, because deriving it needs
///     services: a signature to verify and a payload to parse before the identifier is even readable.
/// </summary>
[Collection("Sequential")]
public sealed class ContextSuppliedIdempotencyKeyTests : LiteBusTestBase
{
    [Fact]
    public async Task A_key_resolved_by_a_guard_deduplicates_repeats()
    {
        var applications = new ApplicationCounter();
        var verifier = new WebhookVerifier();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications, verifier);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ProcessorWebhookCommand { RawBody = "evt_1" }).ConfigureAwait(false);
        await mediator.SendAsync(new ProcessorWebhookCommand { RawBody = "evt_1" }).ConfigureAwait(false);

        applications.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_different_resolved_key_runs_on_its_own()
    {
        var applications = new ApplicationCounter();
        var verifier = new WebhookVerifier();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications, verifier);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ProcessorWebhookCommand { RawBody = "evt_1" }).ConfigureAwait(false);
        await mediator.SendAsync(new ProcessorWebhookCommand { RawBody = "evt_2" }).ConfigureAwait(false);

        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task The_expensive_derivation_runs_once_per_mediation()
    {
        var applications = new ApplicationCounter();
        var verifier = new WebhookVerifier();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications, verifier);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ProcessorWebhookCommand { RawBody = "evt_1" }).ConfigureAwait(false);

        // The shortcut and the completion handler both need the key. Recomputing it per stage would verify the
        // signature twice, which is the cost this design exists to avoid.
        verifier.Verifications.Should().Be(1);
    }

    [Fact]
    public async Task A_declared_context_key_that_no_stage_supplied_is_reported()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(new InMemoryIdempotencyStore(), applications, verifier: null);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator
            .SendAsync(new ProcessorWebhookCommand { RawBody = "evt_1" })
            .ConfigureAwait(false);

        // Reported rather than defaulted: a generated fallback would make every delivery look distinct, so the store
        // would accept them all and the declaration would appear to work while doing nothing.
        await act.Should().ThrowAsync<IdempotencyConfigurationException>()
            .WithMessage("*no pipeline stage supplied one*");
    }

    private static ServiceProvider BuildProvider(
        IIdempotencyStore store,
        ApplicationCounter applications,
        WebhookVerifier? verifier)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(applications);

        if (verifier is not null)
        {
            services.AddSingleton(verifier);
        }

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<ProcessorWebhookCommand>();
                    builder.Register<ProcessorWebhookCommandHandler>();
                    builder.Register<ProcessorWebhookCommandDefinition>();

                    if (verifier is not null)
                    {
                        builder.Register<VerifyProcessorWebhookGuard>();
                    }

                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Stands in for signature verification, and counts how often it ran.
    /// </summary>
    public sealed class WebhookVerifier
    {
        /// <summary>
        ///     The number of verifications performed.
        /// </summary>
        private int _verifications;

        /// <summary>
        ///     Gets the number of verifications performed.
        /// </summary>
        public int Verifications => Volatile.Read(ref _verifications);

        /// <summary>
        ///     Verifies a raw body and returns the provider's event identifier.
        /// </summary>
        /// <param name="rawBody">The unverified request body.</param>
        /// <returns>The event identifier carried by the verified payload.</returns>
        public string Verify(string rawBody)
        {
            Interlocked.Increment(ref _verifications);
            return rawBody;
        }
    }

    public sealed class ProcessorWebhookCommand : ICommand
    {
        /// <summary>
        ///     Gets the unverified request body, which is all the command carries on purpose.
        /// </summary>
        public required string RawBody { get; init; }
    }

    public sealed class ProcessorWebhookCommandDefinition : IIdempotencyDefinition<ProcessorWebhookCommand>
    {
        /// <inheritdoc />
        public IdempotencyDeclaration Idempotency =>
            IdempotencyDeclaration.KeyedByContext() with { Scope = "processor-webhook" };
    }

    private sealed class VerifyProcessorWebhookGuard : ICommandGuard<ProcessorWebhookCommand>
    {
        private readonly IExecutionContext _executionContext;

        private readonly WebhookVerifier _verifier;

        public VerifyProcessorWebhookGuard(WebhookVerifier verifier, IExecutionContext executionContext)
        {
            _verifier = verifier;
            _executionContext = executionContext;
        }

        public Task<Verdict> DecideAsync(
            ProcessorWebhookCommand message,
            CancellationToken cancellationToken = default)
        {
            var eventId = _verifier.Verify(message.RawBody);
            _executionContext.Data.Set(new SuppliedIdempotencyKey(eventId));

            return Task.FromResult(Verdict.Allow);
        }
    }

    private sealed class ProcessorWebhookCommandHandler : ICommandHandler<ProcessorWebhookCommand>
    {
        private readonly ApplicationCounter _applications;

        public ProcessorWebhookCommandHandler(ApplicationCounter applications)
        {
            _applications = applications;
        }

        public Task HandleAsync(ProcessorWebhookCommand message, CancellationToken cancellationToken = default)
        {
            _applications.Count++;
            return Task.CompletedTask;
        }
    }
}
