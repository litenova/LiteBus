using LiteBus.Inbox.Dispatch.AwsSqs;

namespace LiteBus.Inbox.UnitTests.Dispatch.AwsSqs;

/// <summary>
///     Verifies AWS SQS inbox dispatch module registration surface.
/// </summary>
public sealed class AwsSqsInboxDispatchModuleRegistrationTests
{
    /// <summary>
    ///     Verifies the AWS SQS dispatch module exposes builder extensions.
    /// </summary>
    [Fact]
    public void InboxModuleBuilderAwsDispatchExtensions_should_expose_use_aws_sqs_dispatch()
    {
        typeof(InboxModuleBuilderAwsDispatchExtensions)
            .GetMethod(nameof(InboxModuleBuilderAwsDispatchExtensions.UseAwsSqsDispatch))
            .Should().NotBeNull();
    }
}
