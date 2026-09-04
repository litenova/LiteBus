using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that a pre-stage handler can hand a typed value to the main handler through
///     <see cref="IExecutionContext.Data" />.
/// </summary>
/// <remarks>
///     The double-load this removes is the reason authorization tends to stay inside handlers instead of moving into a
///     guard. A guard that has to read an aggregate to decide anything is only affordable if the handler can take the
///     same instance.
/// </remarks>
[Collection("Sequential")]
public sealed class ExecutionContextDataTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering the loader guard, the handler, and the shared load counter.
    /// </summary>
    /// <param name="loads">The counter recording how many times the aggregate was loaded.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(AggregateLoadCounter loads)
    {
        var services = new ServiceCollection();
        services.AddSingleton(loads);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<ArchiveOccurrenceCommand>();
                    builder.Register<ArchiveOccurrenceGuard>();
                    builder.Register<ArchiveOccurrenceCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task A_guard_hands_the_aggregate_it_loaded_to_the_handler()
    {
        var loads = new AggregateLoadCounter();
        var provider = BuildProvider(loads);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ArchiveOccurrenceCommand { OccurrenceId = "occ-1" }).ConfigureAwait(false);

        result.Should().Be("archived occ-1");
        loads.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_denied_message_never_reaches_the_handler_that_would_read_the_value()
    {
        var loads = new AggregateLoadCounter();
        var provider = BuildProvider(loads);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ArchiveOccurrenceCommand { OccurrenceId = "missing" }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        loads.Count.Should().Be(1);
    }

    [Fact]
    public void Get_names_the_missing_type_when_no_stage_supplied_it()
    {
        IHandleContextData data = new HandleContextData();

        var act = () => data.Get<Occurrence>();

        act.Should().Throw<HandleContextDataNotFoundException>()
            .Which.DataType.Should().Be<Occurrence>();
    }

    [Fact]
    public void TryGet_reports_absence_without_throwing()
    {
        IHandleContextData data = new HandleContextData();

        data.TryGet<Occurrence>(out var missing).Should().BeFalse();
        missing.Should().BeNull();

        data.Set(new Occurrence("occ-2"));

        data.TryGet<Occurrence>(out var found).Should().BeTrue();
        found!.Id.Should().Be("occ-2");
    }

    [Fact]
    public void Set_replaces_the_value_stored_under_the_same_type()
    {
        IHandleContextData data = new HandleContextData();

        data.Set(new Occurrence("first"));
        data.Set(new Occurrence("second"));

        data.Get<Occurrence>().Id.Should().Be("second");
        data.Contains<Occurrence>().Should().BeTrue();

        data.Remove<Occurrence>();
        data.Contains<Occurrence>().Should().BeFalse();
    }

    [Fact]
    public void A_keyed_store_holds_several_values_of_one_type()
    {
        IHandleContextData data = new HandleContextData();

        // The identity-map case the unkeyed store cannot express: one command naming two occurrences.
        data.Set("occ-1", new Occurrence("occ-1"));
        data.Set("occ-2", new Occurrence("occ-2"));

        data.Get<Occurrence>("occ-1").Id.Should().Be("occ-1");
        data.Get<Occurrence>("occ-2").Id.Should().Be("occ-2");
        data.Contains<Occurrence>("occ-1").Should().BeTrue();

        data.Remove<Occurrence>("occ-1");
        data.Contains<Occurrence>("occ-1").Should().BeFalse();
        data.Contains<Occurrence>("occ-2").Should().BeTrue();
    }

    [Fact]
    public void A_keyed_value_and_an_unkeyed_value_of_one_type_are_separate_slots()
    {
        IHandleContextData data = new HandleContextData();

        data.Set(new Occurrence("unkeyed"));
        data.Set("occ-1", new Occurrence("keyed"));

        data.Get<Occurrence>().Id.Should().Be("unkeyed");
        data.Get<Occurrence>("occ-1").Id.Should().Be("keyed");

        // Clearing one slot leaves the other, so a stage that stores unkeyed cannot erase a keyed entry by accident.
        data.Remove<Occurrence>();
        data.Contains<Occurrence>().Should().BeFalse();
        data.Contains<Occurrence>("occ-1").Should().BeTrue();
    }

    [Fact]
    public void A_keyed_get_names_the_key_it_could_not_find()
    {
        IHandleContextData data = new HandleContextData();

        var act = () => data.Get<Occurrence>("occ-9");

        var thrown = act.Should().Throw<HandleContextDataNotFoundException>().Which;
        thrown.DataType.Should().Be<Occurrence>();
        thrown.Key.Should().Be("occ-9");
        thrown.Message.Should().Contain("occ-9");
    }

    [Fact]
    public void A_keyed_store_compares_keys_by_value()
    {
        IHandleContextData data = new HandleContextData();

        // An identifier value object is the usual key, because the reader already holds one that is equal but not
        // the same instance.
        data.Set(new OccurrenceId("occ-1"), new Occurrence("occ-1"));

        data.Get<Occurrence>(new OccurrenceId("occ-1")).Id.Should().Be("occ-1");
    }

    [Fact]
    public void A_keyed_member_rejects_a_null_key()
    {
        IHandleContextData data = new HandleContextData();

        var act = () => data.Set(null!, new Occurrence("occ-1"));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Data_is_not_shared_between_mediations()
    {
        var loads = new AggregateLoadCounter();
        var provider = BuildProvider(loads);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // Two sequential mediations each get their own store, so the second cannot read what the first stored. If it
        // could, a guard that returned early would leave a stale aggregate for the next message to act on.
        await mediator.SendAsync(new ArchiveOccurrenceCommand { OccurrenceId = "occ-a" }).ConfigureAwait(false);
        await mediator.SendAsync(new ArchiveOccurrenceCommand { OccurrenceId = "occ-b" }).ConfigureAwait(false);

        loads.Count.Should().Be(2);
        loads.Ids.Should().Equal("occ-a", "occ-b");
    }
}
