using System.Diagnostics;
using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies PostgreSQL inbox recovery across an abrupt worker process termination.
/// </summary>
public sealed class PostgreSqlProcessCrashIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string ContractName = "tests.commands.process-crash";
    private const string MarkerEnvironmentVariable = "LITEBUS_PROCESS_CRASH_MARKER";
    private const string TableEnvironmentVariable = "LITEBUS_PROCESS_CRASH_TABLE";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlProcessCrashIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL test fixture.</param>
    public PostgreSqlProcessCrashIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a command accepted before a worker crash is reclaimed after the abandoned lease expires.
    /// </summary>
    /// <returns>A task that completes when the replacement worker completes the command.</returns>
    [Fact]
    public async Task AcceptedCommand_WhenWorkerProcessIsKilled_ShouldRecoverAfterLeaseExpiry()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var workId = Guid.NewGuid();
        Guid messageId;

        var acceptanceProvider = BuildProvider(options, null, false, "acceptance-worker");
        await using (acceptanceProvider.ConfigureAwait(false))
        {
            var inbox = acceptanceProvider.GetRequiredService<IInbox>();
            var receipt = await inbox.AcceptAsync(new ProcessCrashCommand { WorkId = workId }).ConfigureAwait(false);
            messageId = receipt.Id;
        }

        var markerPath = Path.Combine(Path.GetTempPath(), $"litebus-process-crash-{Guid.NewGuid():N}.marker");
        using var worker = StartCrashWorker(options.TableName, markerPath);

        try
        {
            var enteredHandler = await WaitUntilAsync(
                () => Task.FromResult(File.Exists(markerPath)),
                TestTimeout).ConfigureAwait(false);

            enteredHandler.Should().BeTrue("the child worker should enter the command handler before it is terminated");

            var leasedRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
            leasedRow.Should().NotBeNull();
            leasedRow!.Status.Should().Be(InboxStatus.Processing);
            leasedRow.AttemptCount.Should().Be(1);

            worker.Kill(true);
            await worker.WaitForExitAsync().WaitAsync(TestTimeout).ConfigureAwait(false);

            await WaitForLeaseExpiryAsync(leasedRow.LeaseExpiresAt).ConfigureAwait(false);

            var recoveryProvider = BuildProvider(options, null, false, "replacement-worker");
            await using (recoveryProvider.ConfigureAwait(false))
            {
                var processor = recoveryProvider.GetRequiredService<IInboxProcessor>();
                await processor.ProcessPendingAsync().ConfigureAwait(false);
            }

            var recoveredRow = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, messageId).ConfigureAwait(false);
            recoveredRow.Should().NotBeNull();
            recoveredRow!.Status.Should().Be(InboxStatus.Completed);
            recoveredRow.AttemptCount.Should().Be(2);
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill(true);
                await worker.WaitForExitAsync().WaitAsync(TestTimeout).ConfigureAwait(false);
            }

            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    /// <summary>
    ///     Runs the child worker used by <see cref="AcceptedCommand_WhenWorkerProcessIsKilled_ShouldRecoverAfterLeaseExpiry" />.
    /// </summary>
    /// <returns>A task that normally remains active until the parent test terminates this process.</returns>
    [Fact]
    public async Task ProcessCrashWorker_ShouldHoldLeaseUntilTerminated()
    {
        var markerPath = Environment.GetEnvironmentVariable(MarkerEnvironmentVariable);
        var tableName = Environment.GetEnvironmentVariable(TableEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(markerPath) || string.IsNullOrWhiteSpace(tableName))
        {
            return;
        }

        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions(tableName);
        var provider = BuildProvider(options, markerPath, true, "process-crash-worker");
        await using (provider.ConfigureAwait(false))
        {
            using var workerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await LiteBusHostedServiceExtensions.StartLiteBusHostedServicesAsync(provider, workerTimeout.Token).ConfigureAwait(false);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, workerTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (workerTimeout.IsCancellationRequested)
            {
            }
            finally
            {
                await LiteBusHostedServiceExtensions.StopLiteBusHostedServicesAsync(provider, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private ServiceProvider BuildProvider(
        PostgreSqlInboxStoreOptions options,
        string? markerPath,
        bool enableProcessor,
        string leaseOwner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new ProcessCrashProbe(markerPath));

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<ProcessCrashCommand>();
                commands.Register<ProcessCrashCommandHandler>();
            });

            registry.AddInbox(inbox =>
            {
                inbox.UsePostgreSqlStorage(postgres =>
                {
                    postgres.UseDataSource(_fixture.DataSource);
                    postgres.UseOptions(options);
                    postgres.DisableSchemaInitialization();
                });

                inbox.Contracts.Register<ProcessCrashCommand>(ContractName);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 1,
                    LeaseOwner = leaseOwner,
                    LeaseDuration = LeaseDuration,
                    LeaseHeartbeatInterval = TimeSpan.FromMilliseconds(250),
                    Retry = new RetryOptions { UseJitter = false }
                });
                inbox.UseInProcessDispatch();

                if (enableProcessor)
                {
                    inbox.EnableInboxProcessor(host => host.PollInterval = TimeSpan.FromMilliseconds(50));
                }
            });
        });

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private Process StartCrashWorker(string tableName, string markerPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "LiteBus.Storage.IntegrationTests",
            "LiteBus.Storage.IntegrationTests.csproj");
        var dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetHost,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("test");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-restore");
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("--filter");
        startInfo.ArgumentList.Add(
            $"FullyQualifiedName={typeof(PostgreSqlProcessCrashIntegrationTests).FullName}.{nameof(ProcessCrashWorker_ShouldHoldLeaseUntilTerminated)}");
        startInfo.Environment[PostgreSqlFixture.ConnectionStringEnvironmentVariable] = _fixture.ConnectionString;
        startInfo.Environment[TableEnvironmentVariable] = tableName;
        startInfo.Environment[MarkerEnvironmentVariable] = markerPath;

        return Process.Start(startInfo) ?? throw new InvalidOperationException("The process-crash worker could not be started.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LiteBus.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LiteBus repository root.");
    }

    private static async Task WaitForLeaseExpiryAsync(DateTimeOffset? leaseExpiresAt)
    {
        leaseExpiresAt.Should().HaveValue();
        var delay = leaseExpiresAt!.Value - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(250);

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
        }

        return await condition().ConfigureAwait(false);
    }
}
