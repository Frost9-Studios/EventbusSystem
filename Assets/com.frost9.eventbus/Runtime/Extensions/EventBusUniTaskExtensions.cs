#nullable enable
using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;

namespace Frost9.EventBus
{
    public static class EventBusUniTaskExtensions
    {
        public static async UniTask<T> Next<T>(
            this IEventBus bus,
            Func<T, bool>? predicate = null,
            CancellationToken ct = default)
        {
            var observable = bus.Observe<T>();
            if (predicate == null)
            {
                var taskSource = new UniTaskCompletionSource<T>();
                var subscription = observable.Subscribe(value =>
                {
                    taskSource.TrySetResult(value);
                });
                
                ct.Register(() =>
                {
                    subscription.Dispose();
                    taskSource.TrySetCanceled();
                });
                
                return await taskSource.Task;
            }
            else
            {
                var taskSource = new UniTaskCompletionSource<T>();
                var subscription = observable.Subscribe(value =>
                {
                    if (predicate(value))
                    {
                        taskSource.TrySetResult(value);
                    }
                });
                
                ct.Register(() =>
                {
                    subscription.Dispose();
                    taskSource.TrySetCanceled();
                });
                
                return await taskSource.Task;
            }
        }
    }
}