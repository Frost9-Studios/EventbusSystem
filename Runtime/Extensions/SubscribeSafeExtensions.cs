using System;
using R3;
using UnityEngine;

namespace Frost9.EventBus.Extensions
{
    public static class SubscribeSafeExtensions
    {
        public static IDisposable SubscribeSafe<T>(this IObservable<T> src, Action<T> onNext)
        {
            return src.Subscribe(v =>
            {
                try { onNext(v); }
                catch (Exception ex) { Debug.LogException(ex); }
            });
        }
    }
}