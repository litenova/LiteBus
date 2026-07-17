using System.Net;
using LiteBus.Extensions.AspNetCore;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using InboxProcessorState = LiteBus.Inbox.Abstractions.ProcessorState;
using OutboxProcessorState = LiteBus.Outbox.Abstractions.ProcessorState;

namespace LiteBus.Extensions.UnitTests.AspNetCore;

/// <summary>
///     Verifies ASP.NET management route binding across inbox, outbox, and processor-control surfaces.
/// </summary>
public sealed class ManagementEndpointOperationsTests
{
    /// <summary>
    ///     Verifies manager-backed read and maintenance routes are available for both durable axes.
    /// </summary>
    [Fact]
    public async Task ManagementRoutes_WithBothAxes_ShouldReturnSuccessfulResponses()
    {
        var options = CreateAnonymousOptions();
        using var host = await CreateHostAsync(options).ConfigureAwait(false);
        using var client = host.GetTestClient();
        string[] getPaths =
        [
            "/litebus/inbox/messages?pageSize=10",
            "/litebus/inbox/status-counts",
            "/litebus/inbox/schema",
            "/litebus/inbox/retention/status",
            "/litebus/outbox/messages?pageSize=10",
            "/litebus/outbox/status-counts",
            "/litebus/outbox/schema",
            "/litebus/outbox/retention/status"
        ];
        string[] postPaths =
        [
            "/litebus/inbox/messages/requeue-dead-letters",
            "/litebus/inbox/retention/purge",
            "/litebus/outbox/messages/requeue-dead-letters",
            "/litebus/outbox/retention/purge"
        ];

        foreach (var path in getPaths)
        {
            using var response = await client.GetAsync(path).ConfigureAwait(false);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        }

        foreach (var path in postPaths)
        {
            using var response = await client.PostAsync(path, null).ConfigureAwait(false);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        }
    }

    /// <summary>
    ///     Verifies pause, resume, state, and drain routes invoke the matching axis control with the requested timeout.
    /// </summary>
    [Fact]
    public async Task ProcessorRoutes_WithRegisteredControls_ShouldInvokeBothAxes()
    {
        var inboxControl = new RecordingInboxProcessorControl();
        var outboxControl = new RecordingOutboxProcessorControl();
        using var host = await CreateHostAsync(
            CreateAnonymousOptions(),
            inboxControl: inboxControl,
            outboxControl: outboxControl).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var inboxState = await client.GetAsync("/litebus/inbox/processor/state").ConfigureAwait(false);
        using var outboxState = await client.GetAsync("/litebus/outbox/processor/state").ConfigureAwait(false);
        using var inboxPause = await client.PostAsync("/litebus/inbox/processor/pause", null).ConfigureAwait(false);
        using var outboxPause = await client.PostAsync("/litebus/outbox/processor/pause", null).ConfigureAwait(false);
        using var inboxResume = await client.PostAsync("/litebus/inbox/processor/resume", null).ConfigureAwait(false);
        using var outboxResume = await client.PostAsync("/litebus/outbox/processor/resume", null).ConfigureAwait(false);
        using var inboxDrain = await client.PostAsync(
            "/litebus/inbox/processor/drain?timeoutSeconds=7",
            null).ConfigureAwait(false);
        using var outboxDrain = await client.PostAsync(
            "/litebus/outbox/processor/drain?timeoutSeconds=11",
            null).ConfigureAwait(false);

        HttpResponseMessage[] responses =
        [
            inboxState,
            outboxState,
            inboxPause,
            outboxPause,
            inboxResume,
            outboxResume,
            inboxDrain,
            outboxDrain
        ];
        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        inboxControl.PauseCount.Should().Be(1);
        inboxControl.ResumeCount.Should().Be(1);
        inboxControl.LastDrainTimeout.Should().Be(TimeSpan.FromSeconds(7));
        outboxControl.PauseCount.Should().Be(1);
        outboxControl.ResumeCount.Should().Be(1);
        outboxControl.LastDrainTimeout.Should().Be(TimeSpan.FromSeconds(11));
    }

    /// <summary>
    ///     Verifies an absent durable axis returns the documented fallback response for every path in its route group.
    /// </summary>
    [Fact]
    public async Task InboxRoutes_WhenInboxIsNotConfigured_ShouldReturnNotFound()
    {
        using var host = await CreateHostAsync(
            CreateAnonymousOptions(),
            includeInbox: false,
            includeOutbox: true).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var missingInbox = await client.GetAsync("/litebus/inbox/status-counts").ConfigureAwait(false);
        using var configuredOutbox = await client.GetAsync("/litebus/outbox/status-counts").ConfigureAwait(false);

        missingInbox.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missingInbox.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("Inbox is not configured");
        configuredOutbox.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     Verifies a slash-delimited custom prefix maps management routes without retaining the default prefix.
    /// </summary>
    [Fact]
    public async Task AddLiteBusManagementEndpoints_WithCustomPrefix_ShouldMapOnlyCustomPath()
    {
        var options = CreateAnonymousOptions();
        options.RoutePrefix = "/operations/";
        using var host = await CreateHostAsync(options).ConfigureAwait(false);
        using var client = host.GetTestClient();

        using var customPath = await client.GetAsync("/operations/inbox/status-counts").ConfigureAwait(false);
        using var defaultPath = await client.GetAsync("/litebus/inbox/status-counts").ConfigureAwait(false);

        customPath.StatusCode.Should().Be(HttpStatusCode.OK);
        defaultPath.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static LiteBusManagementOptions CreateAnonymousOptions()
    {
        return new LiteBusManagementOptions
        {
            AllowAnonymousManagement = true,
            FailHealthWhenNoProbes = false
        };
    }

    private static Task<IHost> CreateHostAsync(
        LiteBusManagementOptions options,
        bool includeInbox = true,
        bool includeOutbox = true,
        RecordingInboxProcessorControl? inboxControl = null,
        RecordingOutboxProcessorControl? outboxControl = null)
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

                        if (includeInbox)
                        {
                            registry.AddInboxModule(inbox => inbox.UseInMemoryStorage());
                        }

                        if (includeOutbox)
                        {
                            registry.AddOutboxModule(outbox => outbox.UseInMemoryStorage());
                        }
                    });

                    if (inboxControl is not null)
                    {
                        services.AddSingleton<IInboxProcessorControl>(inboxControl);
                    }

                    if (outboxControl is not null)
                    {
                        services.AddSingleton<IOutboxProcessorControl>(outboxControl);
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.AddLiteBusManagementEndpoints(options));
                });
            });

        return builder.StartAsync();
    }

    private sealed class RecordingInboxProcessorControl : IInboxProcessorControl
    {
        public TimeSpan? LastDrainTimeout { get; private set; }

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public InboxProcessorState State { get; private set; } = InboxProcessorState.Running;

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCount++;
            State = InboxProcessorState.Paused;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCount++;
            State = InboxProcessorState.Running;
            return Task.CompletedTask;
        }

        public Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastDrainTimeout = timeout;
            State = InboxProcessorState.Draining;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOutboxProcessorControl : IOutboxProcessorControl
    {
        public TimeSpan? LastDrainTimeout { get; private set; }

        public int PauseCount { get; private set; }

        public int ResumeCount { get; private set; }

        public OutboxProcessorState State { get; private set; } = OutboxProcessorState.Running;

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCount++;
            State = OutboxProcessorState.Paused;
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCount++;
            State = OutboxProcessorState.Running;
            return Task.CompletedTask;
        }

        public Task DrainAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastDrainTimeout = timeout;
            State = OutboxProcessorState.Draining;
            return Task.CompletedTask;
        }
    }
}
