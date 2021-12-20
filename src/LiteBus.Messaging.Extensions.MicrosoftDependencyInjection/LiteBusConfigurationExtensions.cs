using System;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public static class LiteBusConfigurationExtensions
    {
        public static ILiteBusConfiguration AddMessaging(this ILiteBusConfiguration liteBusConfiguration,
                                                         Action<LiteBusMessageBuilder> builderAction)
        {
            liteBusConfiguration.AddModule(new MessagingModule(builderAction));

            return liteBusConfiguration;
        }
    }
}