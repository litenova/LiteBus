using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Completion;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies that a fault an error handler recovered completes as <see cref="MediationOutcome.Failed" /> with no
///     recorded exception, so completion can tell it apart from a fault that propagated.
/// </summary>
[Collection("Sequential")]
public sealed class RecoveredFaultCompletionTests : LiteBusTestBase
{
    [Fact]
    public async Task Recovered_void_fault_completes_Failed_without_an_exception()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(DirectCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CompletionCommand { ShouldThrow = true }).ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeNull();
        observed.Faulted.Should().BeTrue();
    }

    [Fact]
    public async Task Recovered_result_fault_completes_Failed_without_an_exception_and_keeps_the_value()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(TypedResultCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var result = await mediator
            .SendAsync(new CompletionCommandWithResult { ShouldThrow = true })
            .ConfigureAwait(false);

        result.Should().Be("recovered");

        var observed = recorder.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeNull();
        observed.MessageResult.Should().Be("recovered");
    }

    [Fact]
    public async Task Completion_fault_on_a_recovered_path_propagates()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(ThrowingCompletionHandler));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // The error handler recovers the original fault, so nothing is ending the mediation with an exception and
        // the completion fault must propagate rather than being suppressed into an exception nobody receives.
        var act = async () => await mediator.SendAsync(new CompletionCommand { ShouldThrow = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<NotSupportedException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Unrecovered_fault_still_completes_Failed_with_the_exception()
    {
        var provider = BuildThrowingObserverProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // No error handler is registered, so the original fault propagates and completion still records it. The
        // suppressed completion fault attached to it proves the exception reached the completion stage.
        var act = async () => await mediator.SendAsync(new CompletionCommand { ShouldThrow = true }).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        thrown.Which.Data[MediationExceptionData.SuppressedCompletionFaults]
            .Should().BeAssignableTo<IReadOnlyList<Exception>>().Subject
            .Should().ContainSingle().Which.Should().BeOfType<NotSupportedException>();
    }

    /// <summary>
    ///     Builds a provider whose error handlers recover every fault, with one completion handler.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handler.</param>
    /// <param name="completionHandler">The completion handler type to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CompletionRecorder recorder, Type completionHandler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<CompletionCommand>();
                    builder.Register<CompletionCommandWithResult>();
                    builder.Register<CompletionCommandHandler>();
                    builder.Register<CompletionCommandWithResultHandler>();
                    builder.Register<CompletionCommandErrorHandler>();
                    builder.Register<CompletionCommandWithResultErrorHandler>();
                    builder.Register(completionHandler);
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider whose only completion handler always throws, with no error handler registered.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildThrowingObserverProvider()
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<CompletionCommand>();
                    builder.Register<CompletionCommandHandler>();
                    builder.Register<ThrowingCompletionHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
