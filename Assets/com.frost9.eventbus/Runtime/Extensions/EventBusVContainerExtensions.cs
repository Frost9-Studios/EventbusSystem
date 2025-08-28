using VContainer;

namespace Frost9.EventBus
{
    public static class EventBusVContainerExtensions
    {
        public static void RegisterEventBus(this IContainerBuilder builder)
        {
            builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
        }
    }
}