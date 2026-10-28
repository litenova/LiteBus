using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
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

public sealed class ManagementEndpointLimitsTests
{
    [Fact]
    public void AddLiteBusManagementEndpoints_WithNegativeRunTimeout_ShouldThrowArgumentOutOfRangeException()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddRouting();
        using var app = builder.Build();
        var options = new LiteBusManagementOptions
        {
            DiagnosticChecks = new DiagnosticCheckRunOptions { RunTimeout = TimeSpan.FromSeconds(-1) }
        };

        var act = () => app.AddLiteBusManagementEndpoints(options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("/litebus/inbox/messages")]
    [InlineData("/litebus/outbox/messages")]
    public async Task QueryMessages_WithTooManyMessageIds_ShouldReturnBadRequest(string path)
    {
        var options = new LiteBusManagementOptions
        {
            AllowAnonymousManagement = true,
            FailHealthWhenNoProbes = false,
            MaxBulkMessageIds = 2
        };
        using var host = await CreateHostAsync(options).ConfigureAwait(false);
        using var client = host.GetTestClient();
        var withinLimit = $"{path}?messageIds={Guid.NewGuid()}&messageIds={Guid.NewGuid()}";

        using var allowedResponse = await client.GetAsync(withinLimit).ConfigureAwait(false);
        allowedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var response = await client.GetAsync($"{withinLimit}&messageIds={Guid.NewGuid()}").ConfigureAwait(false);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
        problem.GetProperty("type").GetString().Should().Be("urn:litebus:management:invalid-request");
    }

    private static Task<IHost> CreateHostAsync(LiteBusManagementOptions options)
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
                        registry.AddMessaging(_ =>
                        {
                        });
                        registry.AddInbox(inbox => inbox.UseInMemoryStorage());
                        registry.AddOutbox(outbox => outbox.UseInMemoryStorage());
                    });
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.AddLiteBusManagementEndpoints(options));
                });
            });

        return builder.StartAsync();
    }
}
