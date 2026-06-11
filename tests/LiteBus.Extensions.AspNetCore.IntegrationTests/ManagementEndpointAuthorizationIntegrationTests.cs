using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LiteBus.Extensions.AspNetCore.IntegrationTests;

/// <summary>
///     HTTP integration tests that verify management endpoint authorization defaults.
/// </summary>
public sealed class ManagementEndpointAuthorizationIntegrationTests
{
    /// <summary>
    ///     Verifies that destructive endpoints reject anonymous callers when default options are used.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Theory]
    [InlineData("/litebus/inbox/messages/requeue", "POST")]
    [InlineData("/litebus/inbox/messages", "DELETE")]
    [InlineData("/litebus/inbox/retention/purge", "POST")]
    [InlineData("/litebus/inbox/processor/pause", "POST")]
    public async Task DestructiveEndpoints_ReturnUnauthorized_WhenAnonymousAndDefaultOptions(string path, string method)
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions { FailHealthWhenNoProbes = false });
        using var client = host.GetTestClient();

        var response = await SendAsync(client, method, path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    ///     Verifies that destructive endpoints reject anonymous callers when a named policy is configured.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task DestructiveEndpoints_ReturnUnauthorized_WhenAnonymousAndNamedPolicy()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AuthorizationPolicy = "LiteBusOperator"
        });

        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/litebus/inbox/messages/requeue", new { messageIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    ///     Verifies that destructive endpoints return forbidden when the caller is authenticated but not authorized.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task DestructiveEndpoints_ReturnForbidden_WhenAuthenticatedWithoutPolicyRole()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AuthorizationPolicy = "LiteBusOperator"
        });

        using var client = host.GetTestClient();
        TestAuthHandler.AuthenticationType = "authenticated";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", "token");

        var response = await client.PostAsJsonAsync("/litebus/inbox/messages/requeue", new { messageIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    ///     Verifies that destructive endpoints allow anonymous callers when explicitly opted in.
    /// </summary>
    /// <returns>A task that completes when the assertions finish.</returns>
    [Fact]
    public async Task DestructiveEndpoints_AllowAnonymous_WhenAllowAnonymousManagementIsTrue()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        });

        using var client = host.GetTestClient();

        var response = await client.PostAsJsonAsync("/litebus/inbox/messages/requeue", new { messageIds = Array.Empty<Guid>() });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     Sends an HTTP request using the supplied method name.
    /// </summary>
    /// <param name="client">The test HTTP client.</param>
    /// <param name="method">The HTTP method name.</param>
    /// <param name="path">The request path.</param>
    /// <returns>The HTTP response message.</returns>
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        return method switch
        {
            "POST"   => client.PostAsync(path, null),
            "DELETE" => client.DeleteAsync(path),
            _        => throw new ArgumentOutOfRangeException(nameof(method), method, "Unsupported HTTP method.")
        };
    }

    /// <summary>
    ///     Builds and starts a test host with in-memory storage and management endpoints.
    /// </summary>
    /// <param name="managementOptions">The management endpoint options.</param>
    /// <returns>The started host.</returns>
    private static Task<IHost> CreateHostAsync(LiteBusManagementOptions managementOptions)
    {
        TestAuthHandler.AuthenticationType = null;

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(managementOptions);

                    services.AddAuthentication(TestAuthHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ =>
                        {
                        });

                    services.AddAuthorization(options =>
                    {
                        options.AddPolicy("LiteBusOperator", policy => policy.RequireRole("operator"));
                    });

                    services.AddLiteBus(registry =>
                    {
                        registry.AddMessageModule(_ =>
                        {
                        });

                        registry.AddInboxModule(inbox =>
                        {
                            inbox.Contracts.Register<TestCommand>("tests.command");
                            inbox.UseInMemoryStorage();
                        });

                        registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.AddLiteBusManagementEndpoints(managementOptions));
                });
            });

        return builder.StartAsync();
    }

    /// <summary>
    ///     A sample command type registered for inbox storage during authorization tests.
    /// </summary>
    private sealed class TestCommand;

    /// <summary>
    ///     A test authentication handler that simulates authenticated and operator principals.
    /// </summary>
    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        /// <summary>
        ///     The authentication scheme name used by authorization integration tests.
        /// </summary>
        public const string SchemeName = "Test";

        /// <summary>
        ///     Initializes a new instance of the <see cref="TestAuthHandler" /> class.
        /// </summary>
        /// <param name="options">The monitor for authentication scheme options.</param>
        /// <param name="logger">The logger factory.</param>
        /// <param name="encoder">The URL encoder.</param>
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        /// <summary>
        ///     The principal type to authenticate for the next request, or <see langword="null" /> for anonymous.
        /// </summary>
        public static string? AuthenticationType { get; set; }

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (AuthenticationType is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims = AuthenticationType switch
            {
                "operator"      => [new Claim(ClaimTypes.Role, "operator")],
                "authenticated" => [],
                _               => []
            };

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}