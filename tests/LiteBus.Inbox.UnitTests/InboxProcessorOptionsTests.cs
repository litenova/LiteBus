using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies an unset hook-failure policy resolves to dead-lettering while an explicit value wins.
/// </summary>
public sealed class InboxProcessorOptionsTests
{
    /// <summary>
    ///     Verifies the builder resolves an unset policy to dead-lettering.
    /// </summary>
    [Fact]
    public void ProcessorOptions_ByDefault_ShouldResolveDeadLetter()
    {
        new InboxModuleBuilder().ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.DeadLetter);
    }

    /// <summary>
    ///     Verifies an explicit policy survives the builder.
    /// </summary>
    [Fact]
    public void ProcessorOptions_WhenExplicitPolicySet_ShouldKeepExplicitPolicy()
    {
        var builder = new InboxModuleBuilder();
        builder.UseProcessorOptions(new InboxProcessorOptions
        {
            HookFailurePolicy = ProcessorHookFailurePolicy.CompleteDespiteHookFailure
        });

        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.CompleteDespiteHookFailure);
    }
}
