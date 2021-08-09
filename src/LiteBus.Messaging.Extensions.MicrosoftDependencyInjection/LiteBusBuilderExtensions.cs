using System;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public static class LiteBusBuilderExtensions
    {
        public static ILiteBusBuilder AddMessaging(this ILiteBusBuilder liteBusBuilder,
                                                   Action<LiteBusMessageBuilder> builderAction)
        {
            liteBusBuilder.AddModule(new MessagingModule(builderAction));

            return liteBusBuilder;
        }
    }
}