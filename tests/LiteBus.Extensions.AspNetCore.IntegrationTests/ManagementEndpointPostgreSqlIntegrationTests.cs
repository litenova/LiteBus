using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.Extensions.AspNetCore.IntegrationTests;

/// <summary>
///     HTTP integration tests for LiteBus management endpoints backed by PostgreSQL inbox storage.
/// </summary>
public sealed class ManagementEndpointPostgreSqlIntegrationTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The shared PostgreSQL fixture.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ManagementEndpointPostgreSqlIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public ManagementEndpointPostgreSqlIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that querying inbox messages returns rows persisted in PostgreSQL.
    /// </summary>
    /// <returns>A task that completes when the assertion finishes.</returns>
    [Fact]
    public async Task QueryInboxMessages_ReturnsPersistedRows()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions);

        using var host = await CreateHostAsync(inboxOptions, outboxOptions);
        var inbox = host.Services.GetRequiredService<IInbox>();

        var orderId = Guid.NewGuid();

        var receipt = await inbox.AcceptAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        using var client = host.GetTestClient();
        var response = await client.GetAsync("/litebus/inbox/messages?pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("items").GetArrayLength().Should().Be(1);

        var item = payload.GetProperty("items")[0];
        item.GetProperty("id").GetGuid().Should().Be(receipt.Id);
        item.GetProperty("contractName").GetString().Should().Be("orders.commands.ship");
        item.GetProperty("status").GetInt32().Should().Be((int) InboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies that purge with confirmation deletes rows from PostgreSQL storage.
    /// </summary>
    /// <returns>A task that completes when the assertion finishes.</returns>
    [Fact]
    public async Task Purge_WithConfirm_DeletesRowsInStore()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions);

        using var host = await CreateHostAsync(inboxOptions, outboxOptions);
        var inbox = host.Services.GetRequiredService<IInbox>();

        await inbox.AcceptAsync(new ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = $"ship:{Guid.NewGuid():N}"
        });

        await inbox.AcceptAsync(new ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = $"ship:{Guid.NewGuid():N}"
        });

        using var client = host.GetTestClient();

        var queryBeforePurge = await client.GetAsync("/litebus/inbox/messages?pageSize=50");
        queryBeforePurge.StatusCode.Should().Be(HttpStatusCode.OK);

        (await queryBeforePurge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items")
            .GetArrayLength()
            .Should()
            .Be(2);

        using var purgeRequest = new HttpRequestMessage(HttpMethod.Delete, "/litebus/inbox/messages")
        {
            Content = JsonContent.Create(new { confirm = true })
        };

        var purgeResponse = await client.SendAsync(purgeRequest);
        purgeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await purgeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetInt32().Should().Be(2);

        var queryAfterPurge = await client.GetAsync("/litebus/inbox/messages?pageSize=50");
        queryAfterPurge.StatusCode.Should().Be(HttpStatusCode.OK);

        (await queryAfterPurge.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items")
            .GetArrayLength()
            .Should()
            .Be(0);
    }

    /// <summary>
    ///     Verifies that diagnostic probes registered on the inbox module appear in the health response.
    /// </summary>
    /// <returns>A task that completes when the assertion finishes.</returns>
    [Fact]
    public async Task Health_IncludesRegisteredDiagnosticProbe()
    {
        var inboxOptions = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        var outboxOptions = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, inboxOptions);
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, outboxOptions);

        using var host = await CreateHostAsync(
            inboxOptions,
            outboxOptions,
            inbox => inbox.AddDiagnosticCheck<PostgreSqlInboxSchemaDiagnosticCheck>("litebus.inbox.schema"));

        using var client = host.GetTestClient();
        var response = await client.GetAsync("/litebus/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var probes = payload.EnumerateArray().ToArray();
        probes.Should().Contain(probe => probe.GetProperty("name").GetString() == "litebus.inbox.schema");

        var registeredProbe = probes.Single(probe => probe.GetProperty("name").GetString() == "litebus.inbox.schema");
        registeredProbe.GetProperty("status").GetInt32().Should().Be((int) DiagnosticStatus.Healthy);
        registeredProbe.GetProperty("description").GetString().Should().Contain("schema validation succeeded");
        registeredProbe.GetProperty("data").GetProperty("component").GetString().Should().Be("inbox");
    }

    /// <summary>
    ///     Builds and starts a test host with PostgreSQL storage and management endpoints.
    /// </summary>
    /// <param name="InboxStoreOptions">The PostgreSQL inbox store options.</param>
    /// <param name="OutboxStoreOptions">The PostgreSQL outbox store options.</param>
    /// <param name="configureInbox">An optional callback that configures the inbox module builder.</param>
    /// <returns>The started host.</returns>
    private Task<IHost> CreateHostAsync(
        PostgreSqlInboxStoreOptions InboxStoreOptions,
        PostgreSqlOutboxStoreOptions OutboxStoreOptions,
        Action<InboxModuleBuilder>? configureInbox = null)
    {
        var managementOptions = new LiteBusManagementOptions
        {
            FailHealthWhenNoProbes = false,
            AllowAnonymousManagement = true
        };

        var builder = Host.CreateDefaultBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(managementOptions);

                    services.AddLiteBus(registry =>
                    {
                        registry.AddMessageModule(_ =>
                        {
                        });

                        registry.AddInboxModule(inbox =>
                        {
                            inbox.UsePostgreSqlStorage(postgres =>
                            {
                                postgres.UseDataSource(_fixture.DataSource);
                                postgres.UseOptions(InboxStoreOptions);
                            });

                            inbox.Contracts.Register<ShipOrderCommand>("orders.commands.ship");
                            configureInbox?.Invoke(inbox);
                        });

                        registry.AddOutboxModule(outbox =>
                        {
                            outbox.UsePostgreSqlStorage(postgres =>
                            {
                                postgres.UseDataSource(_fixture.DataSource);
                                postgres.UseOptions(OutboxStoreOptions);
                            });
                        });
                    });
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.AddLiteBusManagementEndpoints(managementOptions));
                });
            });

        return builder.StartAsync();
    }

    /// <summary>
    ///     A sample command accepted into the inbox during integration tests.
    /// </summary>
    private sealed record ShipOrderCommand : ICommand
    {
        /// <summary>
        ///     Gets the order identifier carried by the command.
        /// </summary>
        public required Guid OrderId { get; init; }

        /// <summary>
        ///     Gets the optional idempotency key for duplicate detection.
        /// </summary>
        public string? IdempotencyKey { get; init; }
    }
}