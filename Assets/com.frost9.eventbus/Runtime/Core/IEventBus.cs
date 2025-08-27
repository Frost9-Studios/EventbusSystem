using System;
using R3;

namespace Frost9.EventBus
{
    /// <summary>
    /// Generic event bus interface for publishing and observing type-safe events using reactive extensions.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// Publishes an event of type T to all subscribers.
        /// </summary>
        /// <typeparam name="T">The type of event to publish.</typeparam>
        /// <param name="evt">The event data to publish.</param>
        void Publish<T>(in T evt);

        /// <summary>
        /// Creates an observable stream for events of type T.
        /// </summary>
        /// <typeparam name="T">The type of event to observe.</typeparam>
        /// <returns>An observable that emits events of type T.</returns>
        Observable<T> Observe<T>();
    }
}