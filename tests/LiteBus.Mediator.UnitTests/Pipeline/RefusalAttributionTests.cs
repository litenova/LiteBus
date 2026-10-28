using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that a refusal names the guard or validator that decided it.
/// </summary>
[Collection("Sequential")]
public sealed class RefusalAttributionTests : LiteBusTestBase
{
    [Fact]
    public async Task A_denial_names_the_guard_that_denied_it()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new GuardedCommand()).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.DeniedBy.Should().Be<SecondRefusalGuard>();
        thrown.Which.Message.Should().Contain(nameof(SecondRefusalGuard));
    }

    [Fact]
    public async Task A_validation_failure_names_the_first_validator_that_rejected_it()
    {
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new ValidatedCommand()).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);
        thrown.Which.RejectedBy.Should().Be<RejectingValidator>();
        thrown.Which.Message.Should().Contain(nameof(RejectingValidator));
        thrown.Which.Failures.Should().ContainSingle();
    }

    [Fact]
    public void A_denial_without_a_handler_keeps_the_previous_message()
    {
        var exception = new LiteBusMessageDeniedException(typeof(GuardedCommand), "not permitted");

        exception.Message.Should().Be("Mediation of 'GuardedCommand' was denied: not permitted");
        exception.DeniedBy.Should().BeNull();
    }

    /// <summary>
    ///     Builds a provider with a guarded command, whose second guard denies, and a validated command, whose second
    ///     validator rejects.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<GuardedCommand>();
                    builder.Register<GuardedCommandHandler>();
                    builder.Register<FirstRefusalGuard>();
                    builder.Register<SecondRefusalGuard>();
                    builder.Register<ValidatedCommand>();
                    builder.Register<ValidatedCommandHandler>();
                    builder.Register<AcceptingValidator>();
                    builder.Register<RejectingValidator>();
                });
            })
            .BuildServiceProvider();
    }

    private sealed class GuardedCommand : ICommand;

    private sealed class ValidatedCommand : ICommand;

    private sealed class GuardedCommandHandler : ICommandHandler<GuardedCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(GuardedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ValidatedCommandHandler : ICommandHandler<ValidatedCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(ValidatedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FirstRefusalGuard : ICommandGuard<GuardedCommand>
    {
        /// <inheritdoc />
        public Task<Verdict> DecideAsync(GuardedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Verdict.Allow);
        }
    }

    private sealed class SecondRefusalGuard : ICommandGuard<GuardedCommand>
    {
        /// <inheritdoc />
        public Task<Verdict> DecideAsync(GuardedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Verdict.Deny("second guard refused", "SECOND_GUARD"));
        }
    }

    private sealed class AcceptingValidator : ICommandValidator<ValidatedCommand>
    {
        /// <inheritdoc />
        public Task<Validity> ValidateAsync(ValidatedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Validity.Valid);
        }
    }

    private sealed class RejectingValidator : ICommandValidator<ValidatedCommand>
    {
        /// <inheritdoc />
        public Task<Validity> ValidateAsync(ValidatedCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Validity.Invalid("the amount must be positive", "Amount", "AMOUNT"));
        }
    }
}
