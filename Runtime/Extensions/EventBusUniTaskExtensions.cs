using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Frost9.EventBus.Extensions
{
    public static class EventBusUniTaskExtensions
    {
        public static UniTask<T> Next<T>(
            this IEventBus bus,
            Func<T, bool>? predicate = null,
            CancellationToken ct = default)
            => (predicate == null
                ? bus.Observe<T>().FirstAsync()
                : bus.Observe<T>().FirstAsync(predicate))
               .ToUniTask(cancellationToken: ct);
    }
}