using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies the completion handler settles only the claim the same mediation was granted: a conflict loser, a
///     denial, and a validation failure release nothing, while an answer after a grant releases the granted key.
/// </summary>
[Collection("Sequential")]
public sealed class IdempotencySettlementTests : LiteBusTestBase
{
    [Fact]
    public async Task A_conflicting_claim_is_never_released_by_the_loser()
    {
        var applications = new ApplicationCounter();
        var store = new ConflictOnReclaimStore();
        var provider = BuildProvider(store, applications, static _ => { });

        // Delivery A holds the claim while B arrives for the same key.
        await store.TryClaimAsync("ApplyPaymentCommand:p-9", fingerprint: null).ConfigureAwait(false);

        var mediator = provider.GetRequiredService<ICommandMediator>();

        var first = async () => await mediator
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-9" }).ConfigureAwait(false);

        await first.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*still being applied*").ConfigureAwait(false);

        // A's claim survived B's failure, so B still conflicts instead of running.
        var second = async () => await mediator
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-9" }).ConfigureAwait(false);

        await second.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        applications.Count.Should().Be(0);
        store.ReleaseCalls.Should().BeEmpty();
        store.HeldKeys.Should().Contain("ApplyPaymentCommand:p-9");
    }

    [Fact]
    public async Task A_denied_command_never_releases_an_in_flight_claim()
    {
        var applications = new ApplicationCounter();
        var store = new ConflictOnReclaimStore();
        var provider = BuildProvider(
            store,
            applications,
            static builder => { builder.Register<DenyingPaymentGuard>(); });

        await store.TryClaimAsync("ApplyPaymentCommand:p-9", fingerprint: null).ConfigureAwait(false);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-9" }).ConfigureAwait(false);

        // Guards run before the shortcut stage, so the denied mediation never claimed and settles nothing.
        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        applications.Count.Should().Be(0);
        store.ReleaseCalls.Should().BeEmpty();
        store.HeldKeys.Should().Contain("ApplyPaymentCommand:p-9");
    }

    [Fact]
    public async Task An_answered_mediation_releases_the_key_it_claimed()
    {
        var applications = new ApplicationCounter();
        var store = new RecordingGrantStore();
        var provider = BuildProvider(
            store,
            applications,
            static builder => { builder.Register<AnsweringPaymentShortcut>(); });

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApplyPaymentCommand { PaymentId = "p-7" }).ConfigureAwait(false);

        // The idempotency shortcut claimed first and a later shortcut answered, so the handler never ran and the
        // granted key is released rather than leaked.
        applications.Count.Should().Be(0);
        store.ClaimCalls.Should().ContainSingle().Which.Should().Be("ApplyPaymentCommand:p-7");
        store.ReleaseCalls.Should().ContainSingle().Which.Should().Be("ApplyPaymentCommand:p-7");
    }

    /// <summary>
    ///     Builds a provider with idempotency enabled and the given store registered.
    /// </summary>
    /// <param name="store">The store shared with the test.</param>
    /// <param name="applications">The counter recording how many times each handler actually ran.</param>
    /// <param name="configure">Registers the pipeline doubles the test needs.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        IIdempotencyStore store,
        ApplicationCounter applications,
        Action<CommandModuleBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(applications);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<ApplyPaymentCommand>();
                    builder.Register<ApplyPaymentCommandHandler>();
                    builder.Register<ApplyPaymentCommandDefinition>();
                    builder.EnableIdempotency();
                    configure(builder);
                });
            })
            .BuildServiceProvider();
    }

    private sealed class ConflictOnReclaimStore : IIdempotencyStore
    {
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public List<string> ReleaseCalls { get; } = [];

        public IReadOnlyCollection<string> HeldKeys => _claimed;

        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            if (!_claimed.Add(key))
            {
                throw new InvalidOperationException($"the key '{key}' is still being applied");
            }

            return Task.FromResult(IdempotencyClaim.Granted);
        }

        public Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
        {
            _claimed.Remove(key);
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
        {
            ReleaseCalls.Add(key);
            _claimed.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGrantStore : IIdempotencyStore
    {
        public List<string> ClaimCalls { get; } = [];

        public List<string> ReleaseCalls { get; } = [];

        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            ClaimCalls.Add(key);
            return Task.FromResult(IdempotencyClaim.Granted);
        }

        public Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
        {
            ReleaseCalls.Add(key);
            return Task.CompletedTask;
        }
    }

    private sealed class DenyingPaymentGuard : ICommandGuard<ApplyPaymentCommand>
    {
        public Task<Verdict> DecideAsync(ApplyPaymentCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            return Task.FromResult(Verdict.Deny("the test double denies every payment"));
        }
    }

    [HandlerPriority(10)]
    private sealed class AnsweringPaymentShortcut : ICommandShortcut<ApplyPaymentCommand>
    {
        public Task<Shortcut> TryAnswerAsync(ApplyPaymentCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            return Task.FromResult(Shortcut.Answer("answered after the idempotency shortcut claimed"));
        }
    }
}
