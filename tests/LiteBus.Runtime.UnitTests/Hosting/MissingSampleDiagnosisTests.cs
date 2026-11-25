using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Extensions.Microsoft.Hosting;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Runtime.UnitTests.Hosting;

/// <summary>
///     Covers missing-sample diagnosis, lenient missing-sample handling, and registered samples in host validation.
/// </summary>
public sealed class MissingSampleDiagnosisTests
{
    /// <summary>
    ///     Confirms an unsampleable member fails naming the path, the type, and both registrations.
    /// </summary>
    [Fact]
    public void Validate_UnsampleableMember_ShouldNamePathTypeAndRegistrations()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
            messaging.Contracts.Register<BoxedEvent>("test.boxed")));

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(BoxedEvent), StringComparison.Ordinal)
            && failure.Contains(nameof(BoxedEvent.Box), StringComparison.Ordinal)
            && failure.Contains(nameof(SampleBox), StringComparison.Ordinal)
            && failure.Contains("no static factory or constructor accepts sample values", StringComparison.Ordinal)
            && failure.Contains("RegisterSample", StringComparison.Ordinal)
            && failure.Contains(nameof(IMessageSampleFactory), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms an unsampleable message type fails naming the type without a member path.
    /// </summary>
    [Fact]
    public void Validate_UnsampleableRoot_ShouldNameType()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
            messaging.Contracts.Register<OpaqueEvent>("test.opaque")));

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(OpaqueEvent), StringComparison.Ordinal)
            && failure.Contains("cannot be sampled", StringComparison.Ordinal)
            && failure.Contains("RegisterSample", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms lenient missing-sample handling passes validation for an unsampleable contract.
    /// </summary>
    [Fact]
    public void Validate_WithLenientMissingSamples_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
            messaging.Contracts.Register<BoxedEvent>("test.boxed")));

        var act = () => LiteBusHostValidation.Validate(
            services,
            new LiteBusHostValidationOptions { FailOnMissingSample = false });

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a lenient run reports the unsampleable contract as not run with its diagnosis.
    /// </summary>
    [Fact]
    public void ValidateWithResult_WithLenientMissingSamples_ShouldReportNotRun()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
            messaging.Contracts.Register<BoxedEvent>("test.boxed")));

        var result = LiteBusHostValidation.ValidateWithResult(
            services,
            new LiteBusHostValidationOptions { FailOnMissingSample = false });

        result.Failures.Should().BeEmpty();
        result.SkippedRoundTrips.Should().ContainSingle(entry =>
            entry.Contains("not run", StringComparison.Ordinal)
            && entry.Contains(nameof(BoxedEvent.Box), StringComparison.Ordinal)
            && entry.Contains(nameof(SampleBox), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms a strict result reports the missing sample as a failure with no skipped entries.
    /// </summary>
    [Fact]
    public void ValidateWithResult_Strict_ShouldReportFailureWithoutSkipped()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
            messaging.Contracts.Register<BoxedEvent>("test.boxed")));

        var result = LiteBusHostValidation.ValidateWithResult(services);

        result.Failures.Should().ContainSingle();
        result.SkippedRoundTrips.Should().BeEmpty();
    }

    /// <summary>
    ///     Confirms lenient handling still fails a property the serializer drops.
    /// </summary>
    [Fact]
    public void Validate_LenientWithIgnoredProperty_ShouldStillFail()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
            messaging.Contracts.Register<IgnoredNoteEvent>("test.ignored-note")));

        var act = () => LiteBusHostValidation.Validate(
            services,
            new LiteBusHostValidationOptions { FailOnMissingSample = false });

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains(nameof(IgnoredNoteEvent.InternalNote), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms lenient handling still fails a sample factory that throws.
    /// </summary>
    [Fact]
    public void Validate_LenientWithThrowingFactory_ShouldStillFail()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
        {
            messaging.Contracts.Register<BoxedEvent>("test.boxed");
            messaging.UseMessageSampleFactoryInstance(new ThrowingSampleFactory());
        }));

        var act = () => LiteBusHostValidation.Validate(
            services,
            new LiteBusHostValidationOptions { FailOnMissingSample = false });

        act.Should().Throw<LiteBusHostValidationException>();
    }

    /// <summary>
    ///     Confirms a registered sample lets an otherwise unsampleable contract pass validation.
    /// </summary>
    [Fact]
    public void Validate_WithRegisteredSample_ShouldPass()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
        {
            messaging.Contracts.Register<BoxedEvent>("test.boxed");
            messaging.RegisterSample<SampleBox>(() => new SampleBox(500));
        }));

        var act = () => LiteBusHostValidation.Validate(services);

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Confirms a custom factory without detail reports the generic registration guidance.
    /// </summary>
    [Fact]
    public void Validate_NullCustomFactory_ShouldReportGenericGuidance()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
        {
            messaging.Contracts.Register<BoxedEvent>("test.boxed");
            messaging.UseMessageSampleFactoryInstance(NullSampleFactory.Instance);
        }));

        var act = () => LiteBusHostValidation.Validate(services);

        var exception = act.Should().Throw<LiteBusHostValidationException>().Which;
        exception.Failures.Should().ContainSingle(failure =>
            failure.Contains("RegisterSample", StringComparison.Ordinal)
            && failure.Contains(nameof(IMessageSampleFactory), StringComparison.Ordinal)
            && !failure.Contains("member '", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Confirms a lenient run reports a custom factory gap as not run.
    /// </summary>
    [Fact]
    public void ValidateWithResult_LenientNullCustomFactory_ShouldReportNotRun()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry => registry.AddMessaging(messaging =>
        {
            messaging.Contracts.Register<BoxedEvent>("test.boxed");
            messaging.UseMessageSampleFactoryInstance(NullSampleFactory.Instance);
        }));

        var result = LiteBusHostValidation.ValidateWithResult(
            services,
            new LiteBusHostValidationOptions { FailOnMissingSample = false });

        result.Failures.Should().BeEmpty();
        result.SkippedRoundTrips.Should().ContainSingle(entry =>
            entry.Contains("not run", StringComparison.Ordinal));
    }

    private sealed record BoxedEvent(SampleBox Box);

    private sealed class SampleBox
    {
        public SampleBox(int code)
        {
            if (code <= 100)
            {
                throw new ArgumentException("The code must exceed one hundred.", nameof(code));
            }

            Code = code;
        }

        public int Code { get; }
    }

    private sealed class OpaqueEvent
    {
        private OpaqueEvent()
        {
        }
    }

    private sealed record IgnoredNoteEvent
    {
        public string OrderId { get; init; } = string.Empty;

        [JsonIgnore]
        public string InternalNote { get; init; } = string.Empty;
    }

    private sealed class NullSampleFactory : IMessageSampleFactory
    {
        public static NullSampleFactory Instance { get; } = new();

        public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            return new ValueTask<object?>((object?) null);
        }
    }

    private sealed class ThrowingSampleFactory : IMessageSampleFactory
    {
        public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The factory is broken.");
        }
    }
}
