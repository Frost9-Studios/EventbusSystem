using System;
using System.Collections.Generic;
using System.Threading;
using R3;
using UnityEngine;

namespace Frost9.EventBus
{
    public sealed class EventBus : IEventBus, IDisposable
    {
        readonly Dictionary<Type, object> _subjects = new();
        readonly Dictionary<Type, Action> _completers = new();
        readonly int _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        bool _disposed;

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

        public void Publish<T>(in T evt)
        {
            if (_disposed) return;
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