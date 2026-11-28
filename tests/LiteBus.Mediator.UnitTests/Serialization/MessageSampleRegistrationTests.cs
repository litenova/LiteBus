using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Serialization;

/// <summary>
///     Verifies per-type sample registration, its conflict rule, and its composition reporting.
/// </summary>
[Collection("Sequential")]
public sealed class MessageSampleRegistrationTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies that repeating a sample with the same factory composes and uses the sample.
    /// </summary>
    [Fact]
    public async Task An_identical_sample_repeat_composes_and_uses_the_sample()
    {
        Func<SampleBox> sample = () => new SampleBox(500);
        using var provider = Build(messaging => messaging
            .RegisterSample(sample)
            .RegisterSample(sample));

        var factory = provider.GetRequiredService<IMessageSampleFactory>();
        var created = await factory.CreateSampleAsync(typeof(SampleBox)).ConfigureAwait(false);

        created.Should().BeOfType<SampleBox>().Which.Code.Should().Be(500);
    }

    /// <summary>
    ///     Verifies that repeating a sample with a different factory fails naming both call sites.
    /// </summary>
    [Fact]
    public void A_conflicting_sample_repeat_fails_naming_both_sources()
    {
        var act = () => Build(messaging => messaging
            .RegisterSample<SampleBox>(() => new SampleBox(500))
            .RegisterSample<SampleBox>(() => new SampleBox(700)));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*MessageSampleRegistrationTests.cs*Call Replace*");
    }

    /// <summary>
    ///     Verifies that replacing a sample overwrites the recorded factory.
    /// </summary>
    [Fact]
    public async Task ReplaceSample_overwrites_the_recorded_sample()
    {
        using var provider = Build(messaging => messaging
            .RegisterSample<SampleBox>(() => new SampleBox(500))
            .ReplaceSample<SampleBox>(() => new SampleBox(700)));

        var factory = provider.GetRequiredService<IMessageSampleFactory>();
        var created = await factory.CreateSampleAsync(typeof(SampleBox)).ConfigureAwait(false);

        created.Should().BeOfType<SampleBox>().Which.Code.Should().Be(700);
    }

    /// <summary>
    ///     Verifies that the type overload registers a sample from type values.
    /// </summary>
    [Fact]
    public async Task RegisterSample_with_type_registers_from_values()
    {
        using var provider = Build(messaging => messaging
            .RegisterSample(typeof(SampleBox), () => new SampleBox(600)));

        var factory = provider.GetRequiredService<IMessageSampleFactory>();
        var created = await factory.CreateSampleAsync(typeof(SampleBox)).ConfigureAwait(false);

        created.Should().BeOfType<SampleBox>().Which.Code.Should().Be(600);
    }

    /// <summary>
    ///     Verifies that samples together with a custom factory fail composition.
    /// </summary>
    [Fact]
    public void Samples_with_custom_factory_fail_composition()
    {
        var act = () => Build(messaging => messaging
            .RegisterSample<SampleBox>(() => new SampleBox(500))
            .UseMessageSampleFactoryInstance(new NullSampleFactory()));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*RegisterSample*custom factory*silently ignored*");
    }

    /// <summary>
    ///     Verifies that registered samples appear in the composition sections.
    /// </summary>
    [Fact]
    public void CompositionModel_WithSamples_ReportsSamplesSection()
    {
        using var provider = Build(messaging => messaging
            .RegisterSample<SampleBox>(() => new SampleBox(500)));

        var model = provider.GetRequiredService<ICompositionModel>();
        var section = model.Sections.Should().ContainSingle(entry => entry.SectionName == "message-samples").Subject;
        var data = section.GetData().Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;
        var samples = data["samples"].Should().BeAssignableTo<IEnumerable>().Subject;
        var entry = samples.Cast<object?>().Should().ContainSingle().Subject
            .Should().BeAssignableTo<IReadOnlyDictionary<string, object?>>().Subject;

        entry["messageType"].Should().Be(typeof(SampleBox).FullName);
        entry["source"].Should().BeOfType<string>().Which.Should().Contain("MessageSampleRegistrationTests.cs");
    }

    /// <summary>
    ///     Verifies that a default factory fed with samples still reports as the default port.
    /// </summary>
    [Fact]
    public void CompositionModel_WithSamples_ReportsFactoryAsDefault()
    {
        using var provider = Build(messaging => messaging
            .RegisterSample<SampleBox>(() => new SampleBox(500)));

        var model = provider.GetRequiredService<ICompositionModel>();

        model.Ports.Should().ContainSingle(port =>
                port.PortType == typeof(IMessageSampleFactory))
            .Which.Should().Be(new CompositionPortEntry(
                typeof(IMessageSampleFactory),
                typeof(DefaultMessageSampleFactory),
                true));
    }

    /// <summary>
    ///     Verifies that a composition without samples exports no samples section.
    /// </summary>
    [Fact]
    public void CompositionModel_WithoutSamples_OmitsSamplesSection()
    {
        using var provider = Build(_ =>
        {
        });

        var model = provider.GetRequiredService<ICompositionModel>();

        model.Sections.Should().NotContain(entry => entry.SectionName == "message-samples");
    }

    /// <summary>
    ///     Builds a provider over the messaging configuration under test.
    /// </summary>
    /// <param name="messaging">The messaging configuration under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider Build(Action<MessageModuleBuilder> messaging)
    {
        return new ServiceCollection()
            .AddLiteBus(registry => registry.AddMessaging(messaging))
            .BuildServiceProvider();
    }

    /// <summary>
    ///     A box guarded beyond any general rule, sampled only when registered.
    /// </summary>
    private sealed class SampleBox
    {
        /// <summary>
        ///     Initializes a box, accepting codes above one hundred only.
        /// </summary>
        /// <param name="code">The box code.</param>
        public SampleBox(int code)
        {
            if (code <= 100)
            {
                throw new ArgumentException("The code must exceed one hundred.", nameof(code));
            }

            Code = code;
        }

        /// <summary>
        ///     Gets the box code.
        /// </summary>
        public int Code { get; }
    }

    /// <summary>
    ///     A sample factory that samples nothing.
    /// </summary>
    private sealed class NullSampleFactory : IMessageSampleFactory
    {
        /// <inheritdoc />
        public ValueTask<object?> CreateSampleAsync(Type messageType, CancellationToken cancellationToken = default)
        {
            return new ValueTask<object?>((object?) null);
        }
    }
}
