using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies the migration adapter that lets a validator keep throwing while the codebase converts to
///     <see cref="Validity" /> one file at a time.
/// </summary>
[Collection("Sequential")]
public sealed class ThrowingValidatorTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering the given validator types alongside the command and its handler.
    /// </summary>
    /// <param name="validators">The validator types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(params Type[] validators)
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<RemitCommand>();
                    builder.Register<RemitCommandHandler>();

                    foreach (var validator in validators)
                    {
                        builder.Register(validator);
                    }
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task A_valid_message_passes_through_the_adapter()
    {
        var provider = BuildProvider(typeof(LegacyRemitCommandValidator));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RemitCommand { Amount = 10 }).ConfigureAwait(false);
    }

    [Fact]
    public async Task A_thrown_validation_exception_becomes_an_invalid_outcome()
    {
        var provider = BuildProvider(typeof(LegacyRemitCommandValidator));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RemitCommand { Amount = -1 }).ConfigureAwait(false);

        // The whole point of the adapter: the old throwing body still reports Invalid rather than reaching error
        // handlers as a fault.
        var thrown = await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);
        thrown.Which.Failures.Should().ContainSingle()
            .Which.Message.Should().Be("the amount must be positive");
    }

    [Fact]
    public async Task An_unexpected_exception_still_ends_the_mediation_as_a_failure()
    {
        var provider = BuildProvider(typeof(LegacyRemitCommandValidator));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RemitCommand { Amount = 0 }).ConfigureAwait(false);

        // Only the declared exception type is caught. An unexpected one in a validator is a fault, not a verdict about
        // the message, so swallowing it as Invalid would hide a bug behind a validation error.
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("ledger unavailable").ConfigureAwait(false);
    }

    [Fact]
    public async Task An_adapted_validator_and_a_converted_one_both_report_in_the_same_mediation()
    {
        var provider = BuildProvider(
            typeof(LegacyRemitCommandValidator),
            typeof(ConvertedRemitCommandValidator));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new RemitCommand { Amount = -1, Reference = "" }).ConfigureAwait(false);

        // This is what makes a file-by-file migration possible: the stage collects across both shapes, so a half
        // converted codebase behaves correctly rather than only after the last file lands.
        var thrown = await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);
        thrown.Which.Failures.Select(failure => failure.Message).Should()
            .BeEquivalentTo("the amount must be positive", "a reference is required");
    }
}
