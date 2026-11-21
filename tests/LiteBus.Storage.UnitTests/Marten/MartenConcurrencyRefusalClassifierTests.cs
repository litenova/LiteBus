using JasperFx;
using JasperFx.Events;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using Marten.Exceptions;
using Npgsql;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies that the Marten concurrency classifier turns document and event-stream conflicts into denials.
/// </summary>
public sealed class MartenConcurrencyRefusalClassifierTests
{
    [Fact]
    public void Document_concurrency_conflict_is_denied_with_the_conflict_code()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var recognized = classifier.TryClassify(
            new ConcurrencyException("The document was changed by another writer."),
            out var refusal);

        recognized.Should().BeTrue();
        refusal.Outcome.Should().Be(MediationOutcome.Denied);
        refusal.Code.Should().Be(MartenConcurrencyRefusalClassifier.ConcurrencyConflictCode);
        refusal.Code.Should().Be("concurrency.conflict");
    }

    [Fact]
    public void Event_stream_version_conflict_is_denied_with_the_conflict_code()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var recognized = classifier.TryClassify(
            new EventStreamUnexpectedMaxEventIdException("The stream was changed by another writer."),
            out var refusal);

        recognized.Should().BeTrue();
        refusal.Outcome.Should().Be(MediationOutcome.Denied);
        refusal.Code.Should().Be("concurrency.conflict");
        refusal.Reason.Should().Contain("stream");
    }

    [Fact]
    public void Concurrent_update_is_denied_with_the_conflict_code()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var recognized = classifier.TryClassify(
            new ConcurrentUpdateException(new InvalidOperationException("A serialization failure.")),
            out var refusal);

        recognized.Should().BeTrue();
        refusal.Outcome.Should().Be(MediationOutcome.Denied);
        refusal.Code.Should().Be("concurrency.conflict");
    }

    [Fact]
    public void Wrapped_conflict_is_found_anywhere_in_the_inner_chain()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var wrapped = new InvalidOperationException(
            "the commit failed",
            new InvalidOperationException(
                "the session failed",
                new ConcurrencyException("The document was changed by another writer.")));

        var recognized = classifier.TryClassify(wrapped, out var refusal);

        recognized.Should().BeTrue();
        refusal.Code.Should().Be("concurrency.conflict");
    }

    [Fact]
    public void Conflict_after_the_first_failure_of_an_aggregate_is_found()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        // A batched commit reports every failure in one AggregateException, and its InnerException is only the first.
        var aggregate = new AggregateException(
            new InvalidOperationException("an unrelated failure in the same batch"),
            new InvalidOperationException(
                "the session failed",
                new EventStreamUnexpectedMaxEventIdException("The stream was changed by another writer.")));

        var recognized = classifier.TryClassify(aggregate, out var refusal);

        recognized.Should().BeTrue();
        refusal.Code.Should().Be("concurrency.conflict");
    }

    [Fact]
    public void Non_concurrency_fault_passes_through()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var recognized = classifier.TryClassify(new InvalidOperationException("boom"), out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Unique_violation_passes_through()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var violation = new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            "23505",
            constraintName: "ux_orders_reference");

        var recognized = classifier.TryClassify(violation, out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Stream_lock_passes_through()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var recognized = classifier.TryClassify(
            new JasperFx.Events.StreamLockedException(
                Guid.NewGuid(),
                new InvalidOperationException("The stream is locked.")),
            out _);

        recognized.Should().BeFalse();
    }

    [Fact]
    public void Null_exception_is_rejected()
    {
        var classifier = new MartenConcurrencyRefusalClassifier();

        var classify = () => classifier.TryClassify(null!, out _);
        classify.Should().Throw<ArgumentNullException>();
    }
}
