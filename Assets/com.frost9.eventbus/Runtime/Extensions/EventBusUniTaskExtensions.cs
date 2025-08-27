#nullable enable
using Cysharp.Threading.Tasks;
using R3;
using System;
using System.Threading;

namespace Frost9.EventBus
{
    public static class EventBusUniTaskExtensions
    {
        public static UniTask<T> Next<T>(
            this IEventBus bus,
            Func<T, bool>? predicate = null,
            CancellationToken ct = default)
        {
            var task = predicate == null
                ? bus.Observe<T>().FirstAsync()
                : bus.Observe<T>().FirstAsync(predicate);

            return task.AsUniTask().AttachExternalCancellation(ct);
        }
    }
}