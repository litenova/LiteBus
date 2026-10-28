using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Pipeline;

[Collection("Sequential")]
public sealed class MessageContextGuardTests : LiteBusTestBase
{
    [Fact]
    public async Task RunAsyncPreStages_NullDependencies_ThrowsArgumentNullException()
    {
        var act = async () => await ((IMessageDependencies) null!).RunAsyncPreStages(new object(), CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public async Task RunAsyncPreStages_NullMessage_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();

        var act = async () => await dependencies.RunAsyncPreStages(null!, CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task RunAsyncDecisionStages_NullDependencies_ThrowsArgumentNullException()
    {
        var act = async () => await ((IMessageDependencies) null!).RunAsyncDecisionStages(new object(), CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public async Task RunAsyncPostHandlers_NullDependencies_ThrowsArgumentNullException()
    {
        var act = async () => await ((IMessageDependencies) null!).RunAsyncPostHandlers(new object(), null, CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public async Task RunAsyncPostHandlers_NullMessage_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();

        var act = async () => await dependencies.RunAsyncPostHandlers(null!, null, CancellationToken.None).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("message");
    }

    [Fact]
    public void ResolveRefusalResult_NullDependencies_ThrowsArgumentNullException()
    {
        var act = () => ((IMessageDependencies) null!).ResolveRefusalResult<object>(new object(), PipelineDecision.Continue);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public void ResolveRefusalResult_NullMessage_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();

        var act = () => dependencies.ResolveRefusalResult<object>(null!, PipelineDecision.Continue);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task RunAsyncErrorHandlers_NullDependencies_ThrowsArgumentNullException()
    {
        var context = NewExecutionContext();

        var act = async () => await ((IMessageDependencies) null!).RunAsyncErrorHandlers(new object(), null, new InvalidOperationException(), context).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public async Task RunAsyncErrorHandlers_NullMessage_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();
        var context = NewExecutionContext();

        var act = async () => await dependencies.RunAsyncErrorHandlers(null!, null, new InvalidOperationException(), context).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task RunAsyncErrorHandlers_NullFailure_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();
        var context = NewExecutionContext();

        var act = async () => await dependencies.RunAsyncErrorHandlers(new object(), null, null!, context).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("failure");
    }

    [Fact]
    public async Task RunAsyncErrorHandlers_NullExecutionContext_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();

        var act = async () => await dependencies.RunAsyncErrorHandlers(new object(), null, new InvalidOperationException(), null!).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("executionContext");
    }

    [Fact]
    public async Task RunAsyncCompletionHandlers_WithContext_NullDependencies_ThrowsArgumentNullException()
    {
        var context = new MessageCompletionContext
        {
            Message = new object(),
            Outcome = MediationOutcome.Succeeded,
            Duration = TimeSpan.Zero
        };

        var act = async () => await ((IMessageDependencies) null!).RunAsyncCompletionHandlers(context).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public async Task RunAsyncCompletionHandlers_WithContext_NullContext_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();

        var act = async () => await dependencies.RunAsyncCompletionHandlers(null!).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("context");
    }

    [Fact]
    public async Task RunAsyncCompletionHandlers_Full_NullDependencies_ThrowsArgumentNullException()
    {
        var context = NewExecutionContext();

        var act = async () => await ((IMessageDependencies) null!).RunAsyncCompletionHandlers(new object(), context, MediationOutcome.Succeeded, null, null, null, null, TimeSpan.Zero).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("messageDependencies");
    }

    [Fact]
    public async Task RunAsyncCompletionHandlers_Full_NullMessage_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();
        var context = NewExecutionContext();

        var act = async () => await dependencies.RunAsyncCompletionHandlers(null!, context, MediationOutcome.Succeeded, null, null, null, null, TimeSpan.Zero).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task RunAsyncCompletionHandlers_Full_NullExecutionContext_ThrowsArgumentNullException()
    {
        var dependencies = new EmptyMessageDependencies();

        var act = async () => await dependencies.RunAsyncCompletionHandlers(new object(), null!, MediationOutcome.Succeeded, null, null, null, null, TimeSpan.Zero).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
        thrown.Which.ParamName.Should().Be("executionContext");
    }

    private static IExecutionContext NewExecutionContext()
    {
        return new LiteBus.Messaging.Contexts.Execution.ExecutionContext([], new Dictionary<string, object>(), TimeProvider.System, CancellationToken.None);
    }

    private sealed class EmptyMessageDependencies : IMessageDependencies
    {
        public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> MainHandlers { get; } = new LazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor> IndirectMainHandlers { get; } = new LazyHandlerCollection<IMessageHandler, IMainHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> PreStageHandlers { get; } = new LazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor> IndirectPreStageHandlers { get; } = new LazyHandlerCollection<IMessagePreStageHandler, IPreStageHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> PostHandlers { get; } = new LazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor> IndirectPostHandlers { get; } = new LazyHandlerCollection<IMessagePostHandler, IPostHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> ErrorHandlers { get; } = new LazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor> IndirectErrorHandlers { get; } = new LazyHandlerCollection<IMessageErrorHandler, IErrorHandlerDescriptor>([]);

        public ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> RefusalMappers { get; } = new LazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor>([]);

        public ILazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor> IndirectRefusalMappers { get; } = new LazyHandlerCollection<IMessageRefusalMapper, IRefusalMapperDescriptor>([]);

        public ILazyHandlerCollection<IMessageCompletionHandler, ICompletionHandlerDescriptor> CompletionHandlers { get; } = new LazyHandlerCollection<IMessageCompletionHandler, ICompletionHandlerDescriptor>([]);
    }
}
