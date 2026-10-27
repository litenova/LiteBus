using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing.Mediation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.UnitTests.AspNetCore;

public sealed class IdempotencyKeyFilterTests
{
    [Fact]
    public async Task Send_WithSameKeyTwice_RunsHandlerOnce()
    {
        var applications = new ApplicationCounter();
        using var host = await CreateHostAsync(new InMemoryIdempotencyStore(), applications).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var first = await PostCommandAsync(client, "key-1").ConfigureAwait(false);
        using var second = await PostCommandAsync(client, "key-1").ConfigureAwait(false);
        using var third = await PostCommandAsync(client, "key-2").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(2, applications.Count);

        Assert.False(await ReadAnsweredAsync(first).ConfigureAwait(false));
        Assert.True(await ReadAnsweredAsync(second).ConfigureAwait(false));
        Assert.False(await ReadAnsweredAsync(third).ConfigureAwait(false));
    }

    [Fact]
    public async Task Send_WithoutHeader_ReturnsBadRequest()
    {
        var applications = new ApplicationCounter();
        using var host = await CreateHostAsync(new InMemoryIdempotencyStore(), applications).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var response = await PostCommandAsync(client, key: null).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, applications.Count);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        Assert.Equal("urn:litebus:idempotency:missing-key", problem.GetProperty("type").GetString());
        Assert.Equal("Missing idempotency key", problem.GetProperty("title").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Send_WithTooLongKey_ReturnsBadRequest()
    {
        var applications = new ApplicationCounter();
        using var host = await CreateHostAsync(new InMemoryIdempotencyStore(), applications).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var response = await PostCommandAsync(client, new string('k', 256)).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, applications.Count);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        Assert.Equal("urn:litebus:idempotency:key-too-long", problem.GetProperty("type").GetString());
        Assert.Equal("Idempotency key too long", problem.GetProperty("title").GetString());
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Send_WhenClaimInFlight_ReturnsConflict()
    {
        var applications = new ApplicationCounter();
        using var host = await CreateHostAsync(new InFlightIdempotencyStore(), applications).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var response = await PostCommandAsync(client, "key-1").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, applications.Count);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        Assert.Equal("urn:litebus:idempotency:in-flight", problem.GetProperty("type").GetString());
        Assert.Equal("Idempotent request in flight", problem.GetProperty("title").GetString());
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Send_WhenFingerprintMismatches_ReturnsUnprocessableContent()
    {
        var applications = new ApplicationCounter();
        using var host = await CreateHostAsync(new FingerprintMismatchIdempotencyStore(), applications).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var response = await PostCommandAsync(client, "key-1").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.UnprocessableContent, response.StatusCode);
        Assert.Equal(0, applications.Count);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        Assert.Equal("urn:litebus:idempotency:fingerprint-mismatch", problem.GetProperty("type").GetString());
        Assert.Equal("Idempotency key already used", problem.GetProperty("title").GetString());
        Assert.Equal(422, problem.GetProperty("status").GetInt32());
    }

    private static Task<IHost> CreateHostAsync(IIdempotencyStore store, ApplicationCounter applications)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(store);
                    services.AddSingleton(applications);

                    services.AddLiteBus(registry =>
                    {
                        registry.AddMessaging(_ =>
                        {
                        });

                        registry.AddCommands(commands =>
                        {
                            commands.Register<IdempotentTestCommand>();
                            commands.Register<IdempotentTestCommandHandler>();
                            commands.Register<IdempotentTestCommandDefinition>();
                            commands.Register<IdempotencyKeyGuard>();
                            commands.EnableIdempotency();
                        });
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapPost(
                            "/commands",
                            async (HttpContext context, ICommandMediator mediator, IdempotentTestCommand command, CancellationToken cancellationToken) =>
                            {
                                var settings = new CommandMediationSettings().WithIdempotencyKey(context);
                                var result = await mediator.TrySendAsync(command, settings, cancellationToken).ConfigureAwait(false);
                                return result.ToHttpResult(context, () => Results.Ok(new { answered = result.Outcome == MediationOutcome.Answered }));
                            }).WithIdempotencyKey();
                    });
                });
            });

        return builder.StartAsync();
    }

    private static async Task<HttpResponseMessage> PostCommandAsync(HttpClient client, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/commands")
        {
            Content = JsonContent.Create(new { name = "test" })
        };

        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<bool> ReadAnsweredAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        return payload.GetProperty("answered").GetBoolean();
    }

    /// <summary>
    ///     Counts how many times the handler actually ran, which is what the replay tests assert on.
    /// </summary>
    private sealed class ApplicationCounter
    {
        public int Count { get; set; }
    }

    /// <summary>
    ///     A command keyed by the header the endpoint filter read, because the command carries no key of its own.
    /// </summary>
    public sealed class IdempotentTestCommand : ICommand
    {
        /// <summary>
        ///     Gets or sets the payload the handler ignores.
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Declares that a repeat of <see cref="IdempotentTestCommand" /> is recognised by the context-supplied key.
    /// </summary>
    public sealed class IdempotentTestCommandDefinition : IIdempotencyDefinition<IdempotentTestCommand>
    {
        /// <inheritdoc />
        public IdempotencyDeclaration Idempotency => IdempotencyDeclaration.KeyedByContext();
    }

    private sealed class IdempotentTestCommandHandler : ICommandHandler<IdempotentTestCommand>
    {
        private readonly ApplicationCounter _applications;

        public IdempotentTestCommandHandler(ApplicationCounter applications)
        {
            _applications = applications;
        }

        public Task HandleAsync(IdempotentTestCommand message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            _applications.Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class InFlightIdempotencyStore : IIdempotencyStore
    {
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IdempotencyClaim.InFlight);
        }

        public Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FingerprintMismatchIdempotencyStore : IIdempotencyStore
    {
        public Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(IdempotencyClaim.FingerprintMismatch);
        }

        public Task CompleteAsync(string key, string? payload, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
