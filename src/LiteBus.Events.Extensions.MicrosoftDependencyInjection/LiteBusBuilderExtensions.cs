using System;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    public static class LiteBusBuilderExtensions
    {
        public static ILiteBusBuilder AddEvents(this ILiteBusBuilder liteBusBuilder,
                                                Action<LiteBusEventBuilder> builder)
        {
            liteBusBuilder.AddModule(new EventsModule(builder));

            return liteBusBuilder;
        }
    }
}