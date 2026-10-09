using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies a batch-only <c>UseProcessorOptions</c> call keeps the dispatcher hook-failure recommendation.
/// </summary>
public sealed class OutboxHookFailurePolicyTests
{
    [Fact]
    public void ProcessorOptions_WhenBatchSizeSetBeforeDispatcher_ShouldKeepDispatcherRecommendation()
    {
        var builder = new OutboxModuleBuilder();
        builder.UseProcessorOptions(new OutboxProcessorOptions { BatchSize = 50 });
        builder.RegisterDispatcher(new RecommendingDispatcherModule());

        builder.ProcessorOptions.BatchSize.Should().Be(50);
        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.CompleteDespiteHookFailure);
    }

    [Fact]
    public void ProcessorOptions_WhenBatchSizeSetAfterDispatcher_ShouldKeepDispatcherRecommendation()
    {
        var builder = new OutboxModuleBuilder();
        builder.RegisterDispatcher(new RecommendingDispatcherModule());
        builder.UseProcessorOptions(new OutboxProcessorOptions { BatchSize = 50 });

        builder.ProcessorOptions.BatchSize.Should().Be(50);
        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.CompleteDespiteHookFailure);
    }

    [Fact]
    public void ProcessorOptions_WhenExplicitPolicySetBeforeDispatcher_ShouldKeepExplicitPolicy()
    {
        var builder = new OutboxModuleBuilder();
        builder.UseProcessorOptions(new OutboxProcessorOptions
        {
            BatchSize = 50,
            HookFailurePolicy = ProcessorHookFailurePolicy.DeadLetter
        });
        builder.RegisterDispatcher(new RecommendingDispatcherModule());

        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.DeadLetter);
    }

    [Fact]
    public void ProcessorOptions_WhenExplicitPolicySetAfterDispatcher_ShouldKeepExplicitPolicy()
    {
        var builder = new OutboxModuleBuilder();
        builder.RegisterDispatcher(new RecommendingDispatcherModule());
        builder.UseProcessorOptions(new OutboxProcessorOptions
        {
            BatchSize = 50,
            HookFailurePolicy = ProcessorHookFailurePolicy.DeadLetter
        });

        builder.ProcessorOptions.HookFailurePolicy.Should().Be(ProcessorHookFailurePolicy.DeadLetter);
    }

    /// <summary>
    ///     Test dispatcher module that recommends completing despite hook failure, like transport dispatchers.
    /// </summary>
    private sealed class RecommendingDispatcherModule : IOutboxDispatcherModule
    {
        /// <inheritdoc />
        public ProcessorHookFailurePolicy DefaultHookFailurePolicy =>
            ProcessorHookFailurePolicy.CompleteDespiteHookFailure;

        /// <inheritdoc />
        public void Build(IModuleConfiguration configuration)
        {
        }
    }
}
