using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.UnitTests.AspNetCore;

public sealed class ManagementEndpointTests
{
    [Fact]
    public async Task Health_ReturnsDegraded_WhenNoProbesAndFailHealthWhenNoProbesIsTrue()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = true,
            AllowAnonymousManagement = true
        }).ConfigureAwait(false);

        using var client = host.GetTestClient();

        var response = await client.GetAsync("/litebus/health").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReturnsHealthy_WhenNoProbesAndFailHealthWhenNoProbesIsFalse()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        }).ConfigureAwait(false);

        using var client = host.GetTestClient();

        var response = await client.GetAsync("/litebus/health").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_IncludesProbeData_WhenProbeFails()
    {
        using var host = await CreateHostAsync(
            new LiteBusManagementOptions
            {
                FailHealthWhenNoProbes = false,
                AllowAnonymousManagement = true
            },
            inbox => inbox.AddDiagnosticCheck<UnhealthyDiagnosticCheck>("test.unhealthy")).ConfigureAwait(false);

        using var client = host.GetTestClient();

        var response = await client.GetAsync("/litebus/health").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        var firstProbe = payload.EnumerateArray().First();
        Assert.True(firstProbe.TryGetProperty("data", out var data));
        Assert.Equal("test", data.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Requeue_SelectiveIds_ReturnsOk()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        }).ConfigureAwait(false);

        using var client = host.GetTestClient();
        var messageId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            "/litebus/inbox/messages/requeue",
            new { messageIds = new[] { messageId } }).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Purge_WithoutConfirmBody_ReturnsBadRequest()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        }).ConfigureAwait(false);

        using var client = host.GetTestClient();

        var response = await client.DeleteAsync("/litebus/inbox/messages").ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Purge_WithConfirmBody_ReturnsOk()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        }).ConfigureAwait(false);

        using var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Delete, "/litebus/inbox/messages")
        {
            Content = JsonContent.Create(new { confirm = true })
        };

        var response = await client.SendAsync(request).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OutboxPurge_WithFiltersAndConfirmation_ReturnsOk()
    {
        using var host = await CreateHostAsync(new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        }).ConfigureAwait(false);

        using var client = host.GetTestClient();
        var messageId = Guid.NewGuid();
        var createdAfter = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var createdBefore = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var path = $"/litebus/outbox/messages?messageId={messageId}" +
                   "&statuses=Published&contractName=tests.event&topic=shipments" +
                   "&correlationId=correlation&causationId=causation&tenantId=tenant-a" +
                   $"&createdAfter={createdAfter}&createdBefore={createdBefore}";
        using var request = new HttpRequestMessage(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(new { confirm = true })
        };

        using var response = await client.SendAsync(request).ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<IHost> CreateHostAsync(
        LiteBusManagementOptions options,
        Action<InboxModuleBuilder>? configureInbox = null)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(options);

                    services.AddLiteBus(registry =>
                    {
                        registry.AddMessageModule(_ =>
                        {
                        });

                        registry.AddInboxModule(inbox =>
                        {
                            inbox.Contracts.Register<TestCommand>("tests.command");
                            inbox.UseInMemoryStorage();
                            configureInbox?.Invoke(inbox);
                        });

                        registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.AddLiteBusManagementEndpoints());
                });
            });

        return builder.StartAsync();
    }

    private sealed class TestCommand;

    private sealed class UnhealthyDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "test.unhealthy";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "Probe failed.",
                new Dictionary<string, object> { ["reason"] = "test" }));
        }
    }
}
