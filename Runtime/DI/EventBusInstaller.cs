using System;
using VContainer;
using VContainer.Unity;

namespace Frost9.EventBus.DI
{
    [Obsolete("Use EventBusVContainerExtensions.RegisterEventBus() instead")]
    public sealed class EventBusInstaller : IInstaller
    {
        public void Install(IContainerBuilder builder)
        {
            builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
        }
    }
}