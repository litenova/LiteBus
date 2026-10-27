using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies a shortcut can deny when the refusal can only be decided at the shortcut stage, and that such a denial
///     ends the mediation exactly as a guard denial does.
/// </summary>
[Collection("Sequential")]
public sealed class ShortcutDenialTests : LiteBusTestBase
{
    [Fact]
    public async Task A_denying_shortcut_reports_Denied_through_TrySendAsync()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new DeniableCommand();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(command).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.Reason.Should().Be("the shortcut refused the message");
        result.Code.Should().Be("SHORTCUT_REFUSED");
        command.HandlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task A_denying_shortcut_throws_through_SendAsync_naming_the_shortcut()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new DeniableCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Code.Should().Be("SHORTCUT_REFUSED");
        thrown.Which.DeniedBy.Should().Be<DenyingShortcut>();
        thrown.Which.Message.Should().Contain(nameof(DenyingShortcut));

        command.HandlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task A_shortcut_denial_reaches_completion_handlers_as_Denied()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DeniableCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Denied);
        observed.Reason.Should().Be("the shortcut refused the message");
        observed.Code.Should().Be("SHORTCUT_REFUSED");
        observed.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task A_shortcut_denial_is_a_decision_so_error_handlers_do_not_see_it()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DeniableCommandErrorHandler));
        var command = new DeniableCommand();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        // An error handler exists to recover from faults. Letting it see a refusal would let it undo the refusal.
        command.ErrorHandlerRan.Should().BeFalse();
        recorder.Observed.Single().Context.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task A_refusal_mapper_applies_to_a_shortcut_denial()
    {
        var provider = BuildResultProvider(typeof(DenyingResultShortcut), typeof(DeniableResultRefusalMapper));

        var sent = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DeniableResultCommand()).ConfigureAwait(false);

        // The shortcut supplied only a reason and a code; the mapper decided what a refused caller receives.
        sent.Should().Be("refused:SHORTCUT_REFUSED");
    }

    [Fact]
    public async Task A_mapped_shortcut_denial_reports_Denied_with_a_value_through_TrySendAsync()
    {
        var provider = BuildResultProvider(typeof(DenyingResultShortcut), typeof(DeniableResultRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new DeniableResultCommand()).ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.Outcome.Should().Be(MediationOutcome.Denied);
        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("refused:SHORTCUT_REFUSED");
    }

    [Fact]
    public async Task A_denying_typed_shortcut_throws_when_no_mapper_covers_the_command()
    {
        var provider = BuildResultProvider(typeof(DenyingResultShortcut));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DeniableResultCommand()).ConfigureAwait(false);

        // A refusal never owes the caller the value the handler would have produced, so this is a denial rather
        // than the configuration error the equivalent untyped answer produces.
        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .Where(exception => exception.Code == "SHORTCUT_REFUSED"
                && exception.DeniedBy == typeof(DenyingResultShortcut)).ConfigureAwait(false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Shortcut_Deny_requires_a_reason(string reason)
    {
        var untyped = () => Shortcut.Deny(reason);
        var typed = () => Shortcut<string>.Deny(reason);

        untyped.Should().Throw<ArgumentException>();
        typed.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deny_and_Answer_compare_by_value()
    {
        Shortcut.Deny("refused", "REFUSED").IsDenied.Should().BeTrue();
        Shortcut.Deny("refused", "REFUSED").IsAnswered.Should().BeFalse();
        Shortcut<string>.Deny("refused", "REFUSED").IsDenied.Should().BeTrue();
        Shortcut<string>.Deny("refused", "REFUSED").IsAnswered.Should().BeFalse();

        Shortcut.None.IsDenied.Should().BeFalse();
        Shortcut<string>.None.IsDenied.Should().BeFalse();
        Shortcut.Answer("done").IsDenied.Should().BeFalse();
        Shortcut<string>.Answer("value", "cached").IsDenied.Should().BeFalse();

        Shortcut.Deny("refused", "REFUSED").Should().Be(Shortcut.Deny("refused", "REFUSED"));
        Shortcut.Deny("refused", "REFUSED").Should().NotBe(Shortcut.Answer("refused"));
        Shortcut<string>.Deny("refused", "REFUSED").Should().Be(Shortcut<string>.Deny("refused", "REFUSED"));
        Shortcut<string>.Deny("refused", "REFUSED").Should().NotBe(Shortcut<string>.Answer("value", "refused"));

        Shortcut.Deny("refused", "REFUSED").GetHashCode().Should()
            .Be(Shortcut.Deny("refused", "REFUSED").GetHashCode());
        Shortcut<string>.Deny("refused", "REFUSED").GetHashCode().Should()
            .Be(Shortcut<string>.Deny("refused", "REFUSED").GetHashCode());
    }

    /// <summary>
    ///     Builds a provider for the command with no result, denied by a shortcut.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handler.</param>
    /// <param name="extraTypes">Additional types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CompletionRecorder recorder, params Type[] extraTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<DeniableCommand>();
                    builder.Register<DeniableCommandHandler>();
                    builder.Register<DenyingShortcut>();
                    builder.Register<DeniableCommandCompletionHandler>();

                    foreach (var extra in extraTypes)
                    {
                        builder.Register(extra);
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider for the command that produces a result, denied by a typed shortcut.
    /// </summary>
    /// <param name="decisionTypes">The shortcuts and refusal mappers to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildResultProvider(params Type[] decisionTypes)
    {
        var services = new ServiceCollection();

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<DeniableResultCommand>();
                    builder.Register<DeniableResultCommandHandler>();

                    foreach (var decisionType in decisionTypes)
                    {
                        builder.Register(decisionType);
                    }
                });
            })
            .BuildServiceProvider();
    }

    private sealed class DeniableCommand : ICommand
    {
        public bool HandlerRan { get; set; }

        public bool ErrorHandlerRan { get; set; }
    }

    private sealed class DenyingShortcut : ICommandShortcut<DeniableCommand>
    {
        /// <inheritdoc />
        public Task<Shortcut> TryAnswerAsync(DeniableCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            return Task.FromResult(Shortcut.Deny("the shortcut refused the message", "SHORTCUT_REFUSED"));
        }
    }

    private sealed class DeniableCommandHandler : ICommandHandler<DeniableCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(DeniableCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            message.HandlerRan = true;
            return Task.CompletedTask;
        }
    }

    private sealed class DeniableCommandErrorHandler : ICommandErrorHandler<DeniableCommand>
    {
        /// <inheritdoc />
        public Task HandleErrorAsync(
            MessageErrorContext<DeniableCommand, object> context,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Message.ErrorHandlerRan = true;
            return Task.CompletedTask;
        }
    }

    private sealed class DeniableCommandCompletionHandler : ICommandCompletionHandler<DeniableCommand>
    {
        private readonly CompletionRecorder _recorder;

        public DeniableCommandCompletionHandler(CompletionRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task HandleCompletionAsync(
            MessageCompletionContext<DeniableCommand> context,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            _recorder.Observed.Enqueue(("deniable", context.AsUntyped()));
            return Task.CompletedTask;
        }
    }

    private sealed class DeniableResultCommand : ICommand<string>;

    private sealed class DenyingResultShortcut : ICommandShortcut<DeniableResultCommand, string>
    {
        /// <inheritdoc />
        public Task<Shortcut<string>> TryAnswerAsync(
            DeniableResultCommand message,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            return Task.FromResult(Shortcut<string>.Deny("the shortcut refused the message", "SHORTCUT_REFUSED"));
        }
    }

    private sealed class DeniableResultCommandHandler : ICommandHandler<DeniableResultCommand, string>
    {
        /// <inheritdoc />
        public Task<string> HandleAsync(DeniableResultCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("from-handler");
        }
    }

    private sealed class DeniableResultRefusalMapper : ICommandRefusalMapper<DeniableResultCommand, string>
    {
        /// <inheritdoc />
        public string Map(DeniableResultCommand message, Refusal refusal)
        {
            return $"refused:{refusal.Code ?? refusal.Outcome.ToString()}";
        }
    }
}
