using System.Collections.Concurrent;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies that EF Core resources resolved during inbox dispatch are isolated to one scope per message.
/// </summary>
public sealed class EfCoreInboxDispatchScopeIsolationIntegrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxDispatchScopeIsolationIntegrationTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxDispatchScopeIsolationIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms concurrent messages receive distinct scoped contexts and both contexts are disposed after dispatch.
    /// </summary>
    /// <returns>A task that completes after both message scopes have been disposed.</returns>
    [Fact]
    public async Task ProcessPendingAsync_WithConcurrentMessages_ShouldIsolateAndDisposeScopedDbContexts()
    {
        var tableName = $"inbox_scope_{Guid.NewGuid():N}";
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(tableName);
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

        var recorder = new DispatchContextRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped(_ => new DispatchProbeDbContext(
            new DbContextOptionsBuilder<DispatchProbeDbContext>().Options,
            recorder));
        services.AddScoped(_ => new StorageScopeInboxDbContext(
            new DbContextOptionsBuilder<StorageScopeInboxDbContext>()
                .UseNpgsql(EfCorePostgreSqlTestInfrastructure.CreateScopedConnectionString(_fixture.ConnectionString, storeOptions))
                .Options,
            storeOptions));

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(commands =>
            {
                commands.Register<ScopedDispatchCommand>();
                commands.Register<ScopedDispatchCommandHandler>();
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseEntityFrameworkCoreStorage(storage =>
                {
                    storage.UseDbContext<StorageScopeInboxDbContext>();
                    storage.UseOptions(storeOptions);
                });
                inbox.Contracts.Register<ScopedDispatchCommand>("tests.commands.scoped-dispatch");
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 2,
                    DispatcherConcurrency = 2,
                    LeaseOwner = "efcore-dispatch-scope-worker",
                    Retry = new RetryOptions { UseJitter = false }
                });
                inbox.UseInProcessDispatch();
            });
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        await using (provider.ConfigureAwait(false))
        {
            var inbox = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();

            await inbox.AcceptAsync(new ScopedDispatchCommand(Guid.NewGuid())).ConfigureAwait(false);
            await inbox.AcceptAsync(new ScopedDispatchCommand(Guid.NewGuid())).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.UsedContextIds.Should().HaveCount(2);
            recorder.UsedContextIds.Should().OnlyHaveUniqueItems();
            recorder.DisposedContextIds.Should().BeEquivalentTo(recorder.UsedContextIds);
        }
    }

    private sealed record ScopedDispatchCommand(Guid WorkId) : ICommand;

    private sealed class ScopedDispatchCommandHandler : ICommandHandler<ScopedDispatchCommand>
    {
        private readonly DispatchProbeDbContext _dbContext;
        private readonly DispatchContextRecorder _recorder;

        public ScopedDispatchCommandHandler(
            DispatchProbeDbContext dbContext,
            DispatchContextRecorder recorder)
        {
            _dbContext = dbContext;
            _recorder = recorder;
        }

        public Task HandleAsync(ScopedDispatchCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.RecordUse(_dbContext.ContextId.InstanceId);
            return Task.CompletedTask;
        }
    }

    private sealed class DispatchProbeDbContext : DbContext
    {
        private readonly DispatchContextRecorder _recorder;

        public DispatchProbeDbContext(
            DbContextOptions<DispatchProbeDbContext> options,
            DispatchContextRecorder recorder)
            : base(options)
        {
            _recorder = recorder;
        }

        public override void Dispose()
        {
            _recorder.RecordDispose(ContextId.InstanceId);
            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            _recorder.RecordDispose(ContextId.InstanceId);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class StorageScopeInboxDbContext : EfCoreInboxE2eDbContext
    {
        public StorageScopeInboxDbContext(
            DbContextOptions<StorageScopeInboxDbContext> options,
            EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }

    private sealed class DispatchContextRecorder
    {
        private readonly ConcurrentDictionary<Guid, byte> _disposedContextIds = new();
        private readonly ConcurrentDictionary<Guid, byte> _usedContextIds = new();

        public IReadOnlyCollection<Guid> DisposedContextIds => _disposedContextIds.Keys.ToArray();

        public IReadOnlyCollection<Guid> UsedContextIds => _usedContextIds.Keys.ToArray();

        public void RecordDispose(Guid contextId)
        {
            _disposedContextIds.TryAdd(contextId, 0);
        }

        public void RecordUse(Guid contextId)
        {
            _usedContextIds.TryAdd(contextId, 0);
        }
    }
}
