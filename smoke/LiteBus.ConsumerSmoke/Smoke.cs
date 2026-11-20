using LiteBus.Commands.Storage.Marten;
using LiteBus.ConsumerSmoke;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Runs one smoke scenario and reports it.
/// </summary>
internal static class Smoke
{
    /// <summary>
    ///     Runs a scenario and prints its outcome.
    /// </summary>
    /// <param name="name">The scenario name.</param>
    /// <param name="scenario">The scenario to run.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string name, Func<Task> scenario)
    {
        Console.WriteLine($"Running: {name}");
        await scenario().ConfigureAwait(false);
        Console.WriteLine($"Passed:  {name}");
    }

    /// <summary>
    ///     Throws when a smoke expectation does not hold.
    /// </summary>
    /// <param name="condition">The expectation.</param>
    /// <param name="message">What was expected.</param>
    /// <summary>
    ///     Gets the container validation a host applies in the Development environment: scope validation and
    ///     build validation. Every scenario builds its provider with it, so a singleton that captures a scoped
    ///     service fails the smoke the way it fails a consumer's Development host.
    /// </summary>
    internal static ServiceProviderOptions DevelopmentValidation => new()
    {
        ValidateScopes = true,
        ValidateOnBuild = true
    };

    internal static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Smoke expectation failed: {message}");
        }
    }
}
