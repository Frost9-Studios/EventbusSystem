using R3;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Frost9.EventBus
{
    /// <summary>
    /// Main-thread-only implementation of IEventBus using R3 reactive extensions.
    /// Provides type-safe event publishing and subscription with automatic cleanup.
    /// </summary>
    public sealed class EventBus : IEventBus, IDisposable
    {
        readonly Dictionary<Type, object> _subjects = new();
        readonly Dictionary<Type, Action> _completers = new();
        readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        bool _disposed;

        /// <summary>
        /// Creates an observable stream for events of type T. If no subject exists for this type, creates a new one.
        /// </summary>
        /// <typeparam name="T">The type of event to observe.</typeparam>
        /// <returns>An observable that emits events of type T.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the EventBus has been disposed.</exception>
        public Observable<T> Observe<T>()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(EventBus));
            var t = typeof(T);
            if (_subjects.TryGetValue(t, out var o))
                return ((Subject<T>)o).AsObservable();

            var s = new Subject<T>();
            _subjects[t] = s;
            _completers[t] = s.OnCompleted;
            return s.AsObservable();
        }

        /// <summary>
        /// Publishes an event of type T to all subscribers. Must be called from the main thread.
        /// Events published from other threads will be ignored with a warning in development builds.
        /// </summary>
        /// <typeparam name="T">The type of event to publish.</typeparam>
        /// <param name="evt">The event data to publish.</param>
        public void Publish<T>(in T evt)
        {
            if (_disposed) return;  // post-dispose publish is a no-op by design

            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"Publish<{typeof(T).Name}> ignored: not on main thread.");
#endif
                return;
            }
            if (_subjects.TryGetValue(typeof(T), out var o))
                ((Subject<T>)o).OnNext(evt);
        }

        /// <summary>
        /// Disposes the EventBus, completing all active subjects and clearing internal collections.
        /// After disposal, Observe will throw ObjectDisposedException and Publish calls will be ignored.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var complete in _completers.Values) complete();
            _subjects.Clear();
            _completers.Clear();
        }
    }
}