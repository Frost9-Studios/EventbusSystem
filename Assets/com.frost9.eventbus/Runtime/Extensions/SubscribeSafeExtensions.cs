using R3;
using System;
using UnityEngine;

namespace Frost9.EventBus
{
    public static class SubscribeSafeExtensions
    {
        public static IDisposable SubscribeSafe<T>(this Observable<T> src, Action<T> onNext)
        {
            return src.Subscribe(v =>
            {
                try { onNext(v); }
                catch (Exception ex) { Debug.LogException(ex); }
            });
        }
    }
}