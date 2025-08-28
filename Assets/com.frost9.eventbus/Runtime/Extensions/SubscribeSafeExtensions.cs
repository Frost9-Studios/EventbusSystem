using R3;
using System;
using UnityEngine;

namespace Frost9.EventBus
{
    /// <summary>
    /// Extension methods for Observable that provide safe subscription with automatic exception handling.
    /// </summary>
    public static class SubscribeSafeExtensions
    {
        /// <summary>
        /// Subscribes to an observable with automatic exception handling. Any exceptions thrown in the onNext handler
        /// will be logged to Unity's console without breaking the subscription stream.
        /// </summary>
        /// <typeparam name="T">The type of values emitted by the observable.</typeparam>
        /// <param name="source">The observable to subscribe to.</param>
        /// <param name="onNext">The action to invoke for each emitted value.</param>
        /// <returns>An IDisposable that can be used to unsubscribe from the observable.</returns>
        public static IDisposable SubscribeSafe<T>(this Observable<T> source, Action<T> onNext)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (onNext is null) throw new ArgumentNullException(nameof(onNext));

            return source.Subscribe(v =>
            {
                try { onNext(v); }
                catch (Exception ex) { Debug.LogException(ex); }
            });
        }
    }
}