using System.Globalization;
using LiteBus.Events;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests proving the PostgreSQL outbox processor dispatches promptly through
///     <c>NOTIFY</c> and still dispatches through polling when notifications are lost.
/// </summary>
public sealed class PostgreSqlOutboxListenNotifyFallbackTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxListenNotifyFallbackTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlOutboxListenNotifyFallbackTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies an enqueued message is dispatched through the insert notification before a long poll interval elapses.
    /// </summary>
    /// <returns>A task that completes when the row is published through notification wake-up.</returns>
    [SkippableFact]
    public async Task ProcessorBackgroundService_WhenPendingRowInserted_ShouldWakeViaNotifyBeforePollTimeout()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var recorder = new EventRecorder();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var provider = BuildProvider(
            options,
            recorder,
            host =>
            {
                host.PollInterval = TimeSpan.FromSeconds(30);
                host.UseAdaptivePolling = false;
            });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), runCts.Token).ConfigureAwait(false);

                var outbox = provider.GetRequiredService<IOutbox>();
                await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                    new OrderSubmittedIntegrationEvent { OrderId = orderId },
                    messageId)).ConfigureAwait(false);

                var publishedBeforePoll = await PostgreSqlTestInfrastructure.WaitUntilAsync(
                    async () =>
                    {
                        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
                        return row?.Status == OutboxStatus.Published && recorder.Events.Any(@event => @event.OrderId == orderId);
                    },
                    TimeSpan.FromSeconds(8)).ConfigureAwait(false);

                publishedBeforePoll.Should().BeTrue(
                    "the insert notify trigger should wake the processor before the 30 second poll interval");

                recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

                var publishedRow = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
                publishedRow!.Status.Should().Be(OutboxStatus.Published);
                publishedRow.AttemptCount.Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Verifies the processor still dispatches through polling when the insert notification trigger is missing.
    /// </summary>
    /// <returns>A task that completes when the row is published through the poll fallback.</returns>
    [SkippableFact]
    public async Task ProcessorBackgroundService_WhenNotifySuppressed_ShouldDispatchViaPollFallback()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await DropNotifyTriggerAsync(options).ConfigureAwait(false);

        var triggerExists = await NotifyTriggerExistsAsync(options).ConfigureAwait(false);
        triggerExists.Should().BeFalse("the test suppresses notifications by dropping the insert notify trigger");

        var recorder = new EventRecorder();
        var orderId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        var provider = BuildProvider(
            options,
            recorder,
            host =>
            {
                host.PollInterval = TimeSpan.FromSeconds(1);
                host.UseAdaptivePolling = false;
            });
        await using (provider.ConfigureAwait(false))
        {
            using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, runCts.Token).ConfigureAwait(false);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), runCts.Token).ConfigureAwait(false);

                var outbox = provider.GetRequiredService<IOutbox>();
                await outbox.EnqueueAsync(OutboxEnqueueItem<OrderSubmittedIntegrationEvent>.WithIdentity(
                    new OrderSubmittedIntegrationEvent { OrderId = orderId },
                    messageId)).ConfigureAwait(false);

                var publishedViaPoll = await PostgreSqlTestInfrastructure.WaitUntilAsync(
                    async () =>
                    {
                        var row = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
                        return row?.Status == OutboxStatus.Published && recorder.Events.Any(@event => @event.OrderId == orderId);
                    },
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);

                publishedViaPoll.Should().BeTrue(
                    "the processor should dispatch through polling within a bounded multiple of the poll interval when notifications are lost");

                recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

                var publishedRow = await PostgreSqlTableReaders.ReadOutboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
                publishedRow!.Status.Should().Be(OutboxStatus.Published);
                publishedRow.AttemptCount.Should().Be(1);
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Drops the insert notify trigger so enqueues cannot wake the listener, simulating lost notifications.
    /// </summary>
    /// <param name="options">The outbox store options that identify the test table.</param>
    /// <returns>A task that completes when the trigger is dropped.</returns>
    private async Task DropNotifyTriggerAsync(PostgreSqlOutboxStoreOptions options)
    {
        var triggerName = PostgreSqlIdentifier.IndexName(options.TableName, "insert_notify_trg");
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = $"DROP TRIGGER IF EXISTS {triggerName} ON {tableName};";
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Checks whether the insert notify trigger still exists on the test table.
    /// </summary>
    /// <param name="options">The outbox store options that identify the test table.</param>
    /// <returns><see langword="true" /> when the trigger exists; otherwise <see langword="false" />.</returns>
    private async Task<bool> NotifyTriggerExistsAsync(PostgreSqlOutboxStoreOptions options)
    {
        var triggerName = PostgreSqlIdentifier.UnquotedIndexName(options.TableName, "insert_notify_trg");

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = "SELECT COUNT(*) FROM pg_trigger WHERE tgname = @trigger_name AND NOT tgisinternal;";
                command.Parameters.AddWithValue("trigger_name", triggerName);
                return Convert.ToInt32(await command.ExecuteScalarAsync().ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
            }
        }
    }

    /// <summary>
    ///     Builds a provider with a manifest-hosted outbox processor over PostgreSQL storage with listen/notify enabled.
    /// </summary>
    /// <param name="options">The outbox store options that identify the test table.</param>
    /// <param name="recorder">The recorder that captures dispatched events.</param>
    /// <param name="configureHost">The optional host loop timing override.</param>
    /// <returns>The configured service provider.</returns>
    private ServiceProvider BuildProvider(
        PostgreSqlOutboxStoreOptions options,
        EventRecorder recorder,
        Action<OutboxProcessorHostOptions>? configureHost = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<OrderSubmittedEventHandler>();
            });

            registry.AddOutbox(builder =>
            {
                builder.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(options with { UseListenNotify = true });
                    postgres.DisableSchemaInitialization();
                });

                builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted");

                builder.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "pg-listennotify-test-publisher",
                    Retry = new RetryOptions { UseJitter = false }
                });

                builder.UseInProcessDispatch();

                builder.EnableOutboxProcessor(host =>
                {
                    host.PollInterval = TimeSpan.FromMilliseconds(100);
                    configureHost?.Invoke(host);
                });
            });
        });

        return services.BuildServiceProvider();
    }
}
