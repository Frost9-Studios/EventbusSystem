using System;
using VContainer;
using VContainer.Unity;

namespace Frost9.EventBus
{
    /// <summary>
    /// VContainer installer for registering EventBus as a singleton implementation of IEventBus.
    /// </summary>
    public sealed class EventBusInstaller : IInstaller
    {
        /// <summary>
        /// Registers EventBus as a singleton service implementing IEventBus in the VContainer.
        /// </summary>
        /// <param name="builder">The container builder to register services with.</param>
        public void Install(IContainerBuilder builder)
        {
            // Defensive check
            if (builder is null) throw new ArgumentNullException(nameof(builder));

            builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
        }
    }
}