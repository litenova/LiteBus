namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Groups the dispatchable handler contracts by the shape of the call the pipeline makes through them.
/// </summary>
/// <remarks>
///     The family decides which invoker signature a contract binds to, and therefore which of the delegates on
///     <see cref="PipelineDispatch" /> is populated. It is not the same thing as a stage: the whole pre stage is one
///     family, because a guard, a validator, a shortcut, and a pre-handler are all called with the message and all
///     answer with a <see cref="PipelineDecision" />. Which of those four runs a given handler is
///     <see cref="PreStage" />.
/// </remarks>
internal enum PipelineFamily
{
    /// <summary>
    ///     Called with the message, answers with a <see cref="PipelineDecision" />.
    /// </summary>
    PreStage = 0,

    /// <summary>
    ///     Called with the message and the result the main handler produced, answers with nothing.
    /// </summary>
    PostHandler = 1,

    /// <summary>
    ///     Called with a <see cref="MessageCompletionContext" />, answers with nothing.
    /// </summary>
    CompletionHandler = 2,

    /// <summary>
    ///     Called with the message and a <see cref="Refusal" />, answers with the result the caller receives. This is
    ///     the one family that is not a pipeline stage; it runs on the refusal path in place of raising.
    /// </summary>
    RefusalMapper = 3
}
