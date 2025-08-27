using VContainer;
using VContainer.Unity;

namespace Frost9.EventBus
{
    public sealed class EventBusInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
        }
    }
}