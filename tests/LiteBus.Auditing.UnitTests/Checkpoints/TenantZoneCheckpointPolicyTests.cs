using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Checkpoints;

/// <summary>
///     Verifies that the tenant-zone calendar policies read each tenant's zone from the tenant metadata provider of the
///     deciding pass, and that a pass with no provider fails instead of reading every tenant as UTC.
/// </summary>
public sealed class TenantZoneCheckpointPolicyTests
{
    /// <summary>
    ///     When the segments in these tests opened: 2026-11-20 10:00 UTC.
    /// </summary>
    private static readonly DateTimeOffset OpenedAt = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Daily_policy_is_due_at_each_tenants_own_local_midnight()
    {
        var services = new MetadataServices(new Dictionary<string, string?>
        {
            ["tokyo"] = "Asia/Tokyo",
            ["new-york"] = "America/New_York"
        });
        var policy = CheckpointPolicies.DailyInTenantZone(TimeOnly.MinValue);

        // 16:00 UTC is past Tokyo's midnight (15:00 UTC) and before New York's (05:00 UTC the next day).
        var now = new DateTimeOffset(2026, 11, 20, 16, 0, 0, TimeSpan.Zero);

        (await policy.IsDueAsync(Candidate("tokyo", now, services), CancellationToken.None)).Should().BeTrue();
        (await policy.IsDueAsync(Candidate("new-york", now, services), CancellationToken.None)).Should().BeFalse();

        var nextMorning = new DateTimeOffset(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);
        (await policy.IsDueAsync(Candidate("new-york", nextMorning, services), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Zone_change_is_followed_on_the_next_decision()
    {
        var zones = new Dictionary<string, string?> { ["tenant"] = "America/New_York" };
        var services = new MetadataServices(zones);
        var policy = CheckpointPolicies.DailyInTenantZone(TimeOnly.MinValue);
        var now = new DateTimeOffset(2026, 11, 20, 16, 0, 0, TimeSpan.Zero);

        (await policy.IsDueAsync(Candidate("tenant", now, services), CancellationToken.None)).Should().BeFalse();

        zones["tenant"] = "Asia/Tokyo";

        (await policy.IsDueAsync(Candidate("tenant", now, services), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Tenant_without_a_zone_reads_the_local_time_as_utc()
    {
        var services = new MetadataServices(new Dictionary<string, string?> { ["tenant"] = null });
        var policy = CheckpointPolicies.DailyInTenantZone(TimeOnly.MinValue);

        var beforeUtcMidnight = new DateTimeOffset(2026, 11, 20, 23, 0, 0, TimeSpan.Zero);
        var afterUtcMidnight = new DateTimeOffset(2026, 11, 21, 0, 30, 0, TimeSpan.Zero);

        (await policy.IsDueAsync(Candidate("tenant", beforeUtcMidnight, services), CancellationToken.None)).Should().BeFalse();
        (await policy.IsDueAsync(Candidate("tenant", afterUtcMidnight, services), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task Pass_without_a_tenant_metadata_provider_fails_with_a_configuration_fault()
    {
        var policy = CheckpointPolicies.WeeklyInTenantZone(DayOfWeek.Monday, TimeOnly.MinValue);
        var now = new DateTimeOffset(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

        var act = async () => await policy.IsDueAsync(Candidate("tenant", now, services: null), CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<AuditConfigurationException>().WithMessage("*ITenantMetadataProvider*").ConfigureAwait(false);
    }

    [Fact]
    public void Policies_carry_their_labels()
    {
        ((ILabeledCheckpointPolicy) CheckpointPolicies.DailyInTenantZone(TimeOnly.MinValue)).Label.Should().Be("daily");
        ((ILabeledCheckpointPolicy) CheckpointPolicies.WeeklyInTenantZone(DayOfWeek.Monday, TimeOnly.MinValue, "close"))
            .Label.Should().Be("close");
    }

    /// <summary>
    ///     Builds the candidate of a tenant's segment that has records and no checkpoint yet.
    /// </summary>
    /// <param name="tenantId">The tenant the segment belongs to.</param>
    /// <param name="now">The current time.</param>
    /// <param name="services">The services of the deciding pass, or <see langword="null" />.</param>
    /// <returns>The candidate.</returns>
    private static AuditCheckpointCandidate Candidate(string tenantId, DateTimeOffset now, IServiceProvider? services)
    {
        return new AuditCheckpointCandidate(
            $"{tenantId}/2026",
            3,
            3,
            null,
            null,
            now,
            new AuditSegmentInfo($"{tenantId}/2026", tenantId, new Dictionary<string, string>(), OpenedAt),
            services);
    }

    /// <summary>
    ///     Serves a tenant metadata provider that reads zones from a dictionary.
    /// </summary>
    private sealed class MetadataServices : IServiceProvider, ITenantMetadataProvider
    {
        /// <summary>
        ///     The zone name of each tenant.
        /// </summary>
        private readonly Dictionary<string, string?> _zones;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MetadataServices" /> class.
        /// </summary>
        /// <param name="zones">The zone name of each tenant.</param>
        public MetadataServices(Dictionary<string, string?> zones)
        {
            _zones = zones;
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(ITenantMetadataProvider) ? this : null;
        }

        /// <inheritdoc />
        public ValueTask<IReadOnlyDictionary<string, TenantMetadata>> GetManyAsync(
            IReadOnlyList<string> tenantIds,
            CancellationToken cancellationToken)
        {
            Dictionary<string, TenantMetadata> found = [];

            foreach (var tenantId in tenantIds)
            {
                if (_zones.TryGetValue(tenantId, out var zone))
                {
                    found[tenantId] = new TenantMetadata(tenantId, zone, null, new Dictionary<string, string>());
                }
            }

            return ValueTask.FromResult<IReadOnlyDictionary<string, TenantMetadata>>(found);
        }
    }
}
