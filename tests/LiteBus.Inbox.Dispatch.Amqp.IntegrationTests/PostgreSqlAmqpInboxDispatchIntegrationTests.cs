using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.Transport;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;
using LiteBus.Transport.Amqp;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Dispatch.Amqp.IntegrationTests;

/// <summary>
///     End-to-end inbox AMQP dispatch tests with PostgreSQL storage.
/// </summary>
public sealed class PostgreSqlAmqpInboxDispatchIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>, IClassFixture<RabbitMqFixture>
{
    private const string ContractName = "tests.remote-work";
    private const int ContractVersion = 1;
    private const string TestSchemaName = "litebus_tests";

    private readonly PostgreSqlFixture _postgresFixture;
    private readonly RabbitMqFixture _rabbitMqFixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAmqpInboxDispatchIntegrationTests" /> class.
    /// </summary>
    /// <param name="postgresFixture">The shared PostgreSQL container fixture.</param>
    /// <param name="rabbitMqFixture">The shared RabbitMQ container fixture.</param>
    public PostgreSqlAmqpInboxDispatchIntegrationTests(PostgreSqlFixture postgresFixture, RabbitMqFixture rabbitMqFixture)
    {
        _postgresFixture = postgresFixture;
        _rabbitMqFixture = rabbitMqFixture;
    }

    /// <summary>
    ///     Verifies that processing a leased inbox envelope publishes to AMQP and marks the PostgreSQL row completed.
    /// </summary>
    /// <returns>A task that completes when the publish and database assertions succeed.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishToAmqpAndMarkPostgreSqlEnvelopeCompleted()
    {
        var storeOptions = CreateInboxOptions();
        await PostgreSqlInboxSchema.EnsureAsync(_postgresFixture.DataSource, storeOptions);

        var exchangeName = $"litebus.inbox.pg.dispatch.{Guid.NewGuid():N}";
        var queueName = $"litebus.inbox.pg.dispatch.queue.{Guid.NewGuid():N}";
        var routingKey = ContractName;
        var connectionUri = ResolveConnectionUri(_rabbitMqFixture.ConnectionOptions);

        await AmqpTestInfrastructure.DeclareDirectTopologyAsync(
            connectionUri,
            exchangeName,
            queueName,
            routingKey);

        await using var provider = BuildProvider(storeOptions, exchangeName, routingKey);
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var workItemId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        await inbox.AcceptAsync(new RemoteWorkCommand
        {
            WorkItemId = workItemId,
            IdempotencyKey = $"work:{workItemId}"
        }, new InboxOptions
        {
            Id = messageId,
            CorrelationId = "corr-pg-dispatch",
            CausationId = "cause-pg-dispatch",
            TenantId = "tenant-pg"
        });

        await processor.ProcessPendingAsync();

        var (body, headers) = await AmqpTestInfrastructure.ReceiveOneAsync(
            connectionUri,
            queueName,
            TimeSpan.FromSeconds(30));

        body.Should().Contain(workItemId.ToString());
        headers[AmqpHeaders.MessageId].Should().Be(messageId.ToString("D"));

        var row = await ReadInboxAsync(storeOptions, messageId);
        row.Should().NotBeNull();
        row!.Value.Status.Should().Be(InboxStatus.Completed);
        row.Value.AttemptCount.Should().Be(1);
    }

    private ServiceProvider BuildProvider(
        PostgreSqlInboxStoreOptions storeOptions,
        string exchangeName,
        string routingKey)
    {
        return new ServiceCollection()
            .AddLiteBus(modules =>
            {
                modules.AddPostgreSqlInboxStorage(postgres =>
                {
                    postgres.UseDataSource(_postgresFixture.DataSource);
                    postgres.UseOptions(storeOptions);
                    postgres.DisableSchemaInitialization();
                });

                modules.AddInboxModule(inbox =>
                {
                    inbox.Contracts.Register<RemoteWorkCommand>(ContractName, ContractVersion);
                    inbox.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "pg-amqp-dispatch-test",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });

                modules.AddInboxModule(inbox =>
                {
                    inbox.UseTransport(
                        transport =>
                        {
                            transport.DefaultDestination = exchangeName;
                            transport.ResolveRoute = _ => routingKey;
                        },
                        new AmqpTransportModule(_rabbitMqFixture.ConnectionOptions));
                });
            })
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
    }

    private static PostgreSqlInboxStoreOptions CreateInboxOptions()
    {
        return new PostgreSqlInboxStoreOptions
        {
            SchemaName = TestSchemaName,
            TableName = $"inbox_pg_amqp_{Guid.NewGuid():N}",
            ValidateSchemaCreationOnStartup = false
        };
    }

    private async Task<(InboxStatus Status, int AttemptCount)?> ReadInboxAsync(PostgreSqlInboxStoreOptions options, Guid messageId)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await _postgresFixture.DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT status, attempt_count
                               FROM {tableName}
                               WHERE message_id = @message_id;
                               """;
        command.Parameters.AddWithValue("message_id", messageId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ((InboxStatus)reader.GetInt32(0), reader.GetInt32(1));
    }

    private static Uri ResolveConnectionUri(AmqpConnectionOptions connectionOptions)
    {
        if (connectionOptions.Uri is not null)
        {
            return connectionOptions.Uri;
        }

        return new Uri(
            $"amqp://{Uri.EscapeDataString(connectionOptions.UserName)}:{Uri.EscapeDataString(connectionOptions.Password)}@{connectionOptions.HostName}:{connectionOptions.Port}{connectionOptions.VirtualHost}");
    }
}
