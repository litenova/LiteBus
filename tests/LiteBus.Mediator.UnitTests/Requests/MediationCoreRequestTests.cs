using LiteBus.Commands;
using LiteBus.Messaging;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Requests;

/// <summary>
///     Verifies the mediation-core requests: cause forwarding to the handler execution context, builder-registered
///     refusal exception translation, and that a message fault is never classified as a refusal.
/// </summary>
[Collection("Sequential")]
public sealed class MediationCoreRequestTests : LiteBusTestBase
{
    [Fact]
    public async Task A_command_sent_with_a_cause_exposes_it_on_the_handler_execution_context()
    {
        var recorder = new RequestCauseRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<RequestCauseCommand>();
                    builder.Register<RequestCauseCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var cause = new MediationCause("correlation-1", "causation-1", MediationCauseSource.Outbox);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RequestCauseCommand(), new CommandMediationSettings { Cause = cause })
            .ConfigureAwait(false);

        var observed = recorder.Observed.Should().ContainSingle().Subject;
        observed.Cause.Should().Be(cause);
        observed.Parent.Should().BeNull();
    }

    [Fact]
    public async Task A_translator_registered_on_the_builder_turns_a_handler_exception_into_its_refusal()
    {
        var services = new ServiceCollection();

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    messaging.UseRefusalExceptionTranslator<RequestConflictTranslator>();
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<RequestConflictCommand>();
                    builder.Register<RequestConflictCommandHandler>();
                });
            })
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .TrySendAsync(new RequestConflictCommand())
            .ConfigureAwait(false);

        result.IsDenied.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
        result.Reason.Should().Be("the reference is already taken");
        result.Code.Should().Be("CONFLICT");
    }

    [Fact]
    public void A_message_fault_is_never_classified_as_a_refusal()
    {
        var offers = new RequestOfferRecorder();
        IReadOnlyList<IFaultRefusalClassifier> classifiers = [new RequestCatchAllClassifier(offers)];

        var fault = new MessageFaultException(
            typeof(RequestCauseCommand),
            "LOOKUP_MISSING",
            "the permission lookup is not registered");

        MediationExceptionFilters.TryClassifyRefusal(classifiers, fault, out _).Should().BeFalse();

        // The catch-all classifier saw nothing: a message fault never reaches one.
        offers.Offered.Should().BeEmpty();
    }
}
