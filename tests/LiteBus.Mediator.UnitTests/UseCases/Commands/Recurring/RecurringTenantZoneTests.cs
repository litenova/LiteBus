using LiteBus.Commands.Recurring;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers per-tenant schedule zones: resolving zones from tenant metadata, the UTC fallback for tenants with no
///     record, no zone, or an unknown zone, the system zone resolver, and the services overload.
/// </summary>
public sealed class RecurringTenantZoneTests
{
    [Fact]
    public async Task FromMetadata_resolves_the_zone_stored_on_the_tenant_record()
    {
        var provider = new ScriptedMetadataProvider(
            new TenantMetadata("acme", "UTC", null, new Dictionary<string, string>()));
        var zoneOf = TenantZones.FromMetadata(provider);

        var zone = await zoneOf("acme", CancellationToken.None).ConfigureAwait(false);

        zone.Should().NotBeNull();
        zone!.Id.Should().Be("UTC");
    }

    [Fact]
    public async Task FromMetadata_without_record_reads_as_utc_and_warns_once()
    {
        var provider = new ScriptedMetadataProvider();
        var logger = new RecordingLogger();
        var zoneOf = TenantZones.FromMetadata(provider, logger: logger);

        var first = await zoneOf("ghost", CancellationToken.None).ConfigureAwait(false);
        var second = await zoneOf("ghost", CancellationToken.None).ConfigureAwait(false);

        first.Should().BeNull();
        second.Should().BeNull();

        var warnings = logger.Entries.Where(entry => entry.Id.Id == 2121).ToList();
        warnings.Should().HaveCount(1);
        warnings[0].Level.Should().Be(LogLevel.Warning);
        warnings[0].Message.Should().Contain("ghost");
    }

    [Fact]
    public async Task FromMetadata_without_zone_reads_as_utc()
    {
        var provider = new ScriptedMetadataProvider(
            new TenantMetadata("acme", null, null, new Dictionary<string, string>()));
        var zoneOf = TenantZones.FromMetadata(provider);

        var zone = await zoneOf("acme", CancellationToken.None).ConfigureAwait(false);

        zone.Should().BeNull();
    }

    [Fact]
    public async Task FromMetadata_with_unknown_zone_reads_as_utc()
    {
        var provider = new ScriptedMetadataProvider(
            new TenantMetadata("acme", "Never/Heard", null, new Dictionary<string, string>()));
        var logger = new RecordingLogger();
        var zoneOf = TenantZones.FromMetadata(provider, logger: logger);

        var zone = await zoneOf("acme", CancellationToken.None).ConfigureAwait(false);

        zone.Should().BeNull();
        logger.Entries.Should().ContainSingle(entry => entry.Id.Id == 2121);
    }

    [Fact]
    public async Task FromMetadata_uses_a_replacement_resolver()
    {
        var provider = new ScriptedMetadataProvider(
            new TenantMetadata("acme", "X-PlusFive", null, new Dictionary<string, string>()));
        var expected = TimeZoneInfo.CreateCustomTimeZone("TenantZoneTests-PlusFive", TimeSpan.FromHours(5), "Plus five", "Plus five");
        var zoneOf = TenantZones.FromMetadata(provider, new ScriptedZoneResolver(expected));

        var zone = await zoneOf("acme", CancellationToken.None).ConfigureAwait(false);

        zone.Should().BeSameAs(expected);
    }

    [Fact]
    public void FromMetadata_rejects_a_null_provider()
    {
        var act = () => TenantZones.FromMetadata(provider: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromMetadata_services_overload_throws_without_a_provider()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => TenantZones.FromMetadata(provider);

        act.Should().Throw<LiteBusDependencyResolutionException>();
    }

    [Fact]
    public async Task FromMetadata_services_overload_uses_the_registered_services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantMetadataProvider>(new ScriptedMetadataProvider(
            new TenantMetadata("acme", "UTC", null, new Dictionary<string, string>())));
        using var provider = services.BuildServiceProvider();

        var zone = await TenantZones.FromMetadata(provider)("acme", CancellationToken.None).ConfigureAwait(false);

        zone.Should().NotBeNull();
        zone!.Id.Should().Be("UTC");
    }

    [Fact]
    public void SystemTimeZoneResolver_resolves_a_known_name()
    {
        SystemTimeZoneResolver.Instance.Resolve("UTC").Should().NotBeNull();
    }

    [Fact]
    public void SystemTimeZoneResolver_returns_null_for_an_unknown_name()
    {
        SystemTimeZoneResolver.Instance.Resolve("Never/Heard").Should().BeNull();
    }

    [Fact]
    public void SystemTimeZoneResolver_rejects_a_blank_name()
    {
        var act = () => SystemTimeZoneResolver.Instance.Resolve("  ");

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Captures log entries so a test can assert the unknown-zone warning.
    /// </summary>
    public sealed class RecordingLogger : ILogger
    {
        /// <summary>
        ///     The entries logged so far, in the order they were written.
        /// </summary>
        private readonly List<RecordedEntry> _entries = [];

        /// <summary>
        ///     Gets the entries logged so far, in the order they were written.
        /// </summary>
        public IReadOnlyList<RecordedEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return new NullScope();
        }

        /// <summary>
        ///     Determines whether the logger records the supplied level.
        /// </summary>
        /// <param name="logLevel">The level to check.</param>
        /// <returns>Always <see langword="true" />.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        ///     Records one log entry.
        /// </summary>
        /// <typeparam name="TState">The type of the logged state.</typeparam>
        /// <param name="logLevel">The level the entry was logged at.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The logged state.</param>
        /// <param name="exception">The logged exception, when any.</param>
        /// <param name="formatter">Renders the state and the exception as a message.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new RecordedEntry(logLevel, eventId, formatter(state, exception)));
            }
        }

        /// <summary>
        ///     A scope that releases nothing.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    ///     One captured log entry.
    /// </summary>
    public sealed record RecordedEntry(LogLevel Level, EventId Id, string Message);

    private sealed class ScriptedMetadataProvider : ITenantMetadataProvider
    {
        private readonly Dictionary<string, TenantMetadata> _records;

        public ScriptedMetadataProvider(params TenantMetadata[] records)
        {
            _records = records.ToDictionary(record => record.TenantId, StringComparer.Ordinal);
        }

        public ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
            IReadOnlyList<string> tenantIds,
            CancellationToken cancellationToken)
        {
            var found = tenantIds
                .Where(id => _records.ContainsKey(id))
                .ToDictionary(id => id, id => _records[id], StringComparer.Ordinal);

            return new ValueTask<IReadOnlyDictionary<string, TenantMetadata>>(found);
        }
    }

    private sealed class ScriptedZoneResolver : ITimeZoneResolver
    {
        private readonly TimeZoneInfo _zone;

        public ScriptedZoneResolver(TimeZoneInfo zone)
        {
            _zone = zone;
        }

        public TimeZoneInfo? Resolve(string timeZoneId)
        {
            return _zone;
        }
    }
}
